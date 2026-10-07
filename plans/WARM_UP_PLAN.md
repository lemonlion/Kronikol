# Warm-up plan: #113

**Written:** 2026-10-06, at 4.6.0 (`37e93813`), six days after #113 was filed. **Status: green-lit 2026-10-07**, when
the owner asked for the plan in full and chose A for Q5; every other question is taken as recommended (`ROADMAP.md` row
1.19, decision D33). Executing in the order R0, R1, R2; section 11 logs each release. Evidence labels: **RUN** (measured here; the script, the data it read and what
it printed are in the harness), **READ** (in the source at `37e93813`, `file:line`), **INFERRED** (reasoned from facts,
stated by none), **ISSUE** (taken from #113, not re-measured), **DOC** (a third party's documentation). The harness is
[`WARM_UP_PLAN.harness/`](WARM_UP_PLAN.harness/README.md). Its `warmup.py` is the rule this plan proposes, runnable over
any `TestRunReport.json`, and is the spec the implementation is checked against (S7).

#113 asks Kronikol to notice when a scenario's duration carries the app's one-time warm-up (the first request down a
path paying for JIT, a schema build or a first connection) and to say so wherever a duration is ranked, without
changing `durationSeconds`. The ask is right and buildable, and every number in the issue that this plan re-measured
held (section 1). The plan tested the issue's rule on a newer CI run of the same consumer (18 lanes), on local runs
(three ReqNRoll, two xUnit, two with the documented remedy) and on a second app, and changes it in three places the data
asks for:

- **One later call is baseline enough.** The issue asks for two. BreakfastProvider calls
  `GET /customer-preferences/{id}` twice per run, so under the issue's rule its own parallel-lane example, *Retrieving
  non-existent customer preferences should return not found* (median 778 ms over 50 ReqNRoll runs, 3 to 23 ms under the
  serial frameworks), is never explained (F1).
- **A call that waited for the warm-up is released with it.** The issue's waiter test (10 times the median) misses a
  `POST /graphql` that ended 0.7 ms after the run's first, because one GraphQL path carries operations of unequal cost.
  Ending within 50 ms of the first call is a second test (F2).
- **The name.** "Cold start" already names a history stream with too few runs, in a public property and in
  `query history --json` (F6). This plan says *first-call warm-up* in prose and `warmUp` in data.

Measured against the other frameworks as ground truth, the rule is precise and partial. It never took more from an
in-memory scenario than the other frameworks say it should, and it explains 25 of the 57 scenarios that ran anomalously
slow in one in-memory lane (the issue's rule: 15). The other 32 are warm-ups no rule over one run's calls can see: a
path called once, an event the app consumed while the test polled, a second host warming up, a call slowed by another
path's warm-up (F3). So the report presents the mark as part of a duration it can account for, never as proof that what
is left is warm.

The research also found live defects outside the ask. On a machine whose culture writes a decimal comma, XML and YAML
durations stop being numbers, every Scenario Timeline bar is drawn 2 px wide, and `kronikol query` prints `1,27 s`
(F12, RUN). They sit on lines this plan edits, so they ship first, as their own patch.

## 0. Summary

| | |
|---|---|
| What ships | **R0, a patch:** durations written the same on every machine (F12). **R1, a minor:** the warm-up pass, its marks in `TestRunReport.json` (and YAML, XML, both schemas and the mergeable file), and the CLI: `summary` ranks Slowest without the warm-up and says where it went, `flow` and `interactions` mark the calls. **R2, a minor:** the HTML: a marker on the duration badge, the warm-up shaded in the Scenario Timeline, the P50 to P99 filter ranked without it |
| The rule | Section 4.1. For each shape of call the scenario's actor makes (service, method, path with ids and numbers folded), the run's first call is a warm-up when at least one later call of the shape exists and the first took at least 10 times and 50 ms more than their median. A call of the shape that started while the first ran is one too when it is as far above the median, or ended within 50 ms of the first |
| What it records | `warmUpSeconds` on a scenario that carries a warm-up and `warmUp` on each marked request, nothing on anything else. `durationSeconds` is unchanged |
| What it cannot see | A path called once, an event-driven wait, a second host, cross-path contention, a first success after a first failure (4.6). The docs say so, and how to avoid all of them (4.8) |
| Bumps | R0 patch, R1 minor, R2 minor (6.2). No new option |
| Waits for | Nothing, to start. #122 (BDDfy times an async step to its first `await`) before R2's consumer acceptance: until it is fixed, 31 BDDfy scenarios carry more warm-up than their recorded duration (F7) |
| Open | Nothing. Q1 to Q15 (section 9) were answered on 2026-10-07: Q5 by the owner (A, the warm-up as muted text in the badge), the rest as recommended |

## 1. How far each claim was checked

| # | The issue says | Level | Verdict |
|---|---|---|---|
| C1 | *Order status via grpc should return order details* has an xUnit median of 1,193 ms over 45 runs and 24 to 82 ms under the other frameworks; the first table's other five rows | RUN | **Reproduced** from the consumer's ledger (`kronikol-history` at `c2e7db61`): 1,193 ms (671 to 1,325) over 49 passing runs from 2026-09-14 to 2026-09-30, every other cell within a few ms of the issue's. This filter counts 48 to 50 runs per lane where the issue counts 44 to 46. Through 2026-10-06 no median moves by more than 29 ms (`results/ledger-*.txt`) |
| C2 | Kronikol's timers are not the cause: every adapter's recorded window matches its calls, BDDfy apart (#122) | ISSUE, partly RUN | Not re-measured the issue's way. Consistent with it: over the 18 lanes of the 2026-10-05 run, the only scenarios whose warm-up calls outlast their recorded duration are BDDfy's, 31 of them (`results/totals.txt`) |
| C3 | The first request down a path costs 55 to 115 times what it costs afterwards | RUN | **Holds on a newer run** (2026-10-05). xUnit lane: the first `POST /orders` 661 ms against a median of 7.6 ms over the 39 after it (87 times); the first `POST /graphql` 746 ms against 7.6 (`results/marks-ci.txt`) |
| C4 | 3 to 5 of each lane's 10 slowest made the run's first call down a path | RUN | **Holds.** 3 to 6 of the 10 slowest in each in-memory lane carry a warm-up, and 3 to 5 of them leave the ten when ranked by the time left; BDDfy's lane, 2 and 1 (#122) (`results/slowest.txt`) |
| C5 | It moves with test order, and a local run disagrees with CI | RUN | **Holds.** In both local xUnit runs the first `POST /orders` lands in *Audit logs should be filterable by entity id*; in CI's run of 2026-10-05, in *Order status via grpc...* |
| C6 | In the parallel lanes it moves every run | RUN | **Holds.** Over three local ReqNRoll runs, 56 of 166 scenario names move by 200 ms or more (`results/stability.txt`) |
| C7 | The rule: two later calls, 10 times and 50 ms above their median | RUN | **Precise, and misses its own example.** No over-subtraction in five in-memory lanes; explains 15 of 57 anomalies; never marks the customer-preferences row (F1) |
| C8 | The overlap step catches ReqNRoll's second 2.06 s scenario | ISSUE, and RUN on a newer run | That run's reports are gone (F14). On 2026-10-05 the same pattern is caught only with a second test (F2) |
| C9 | "A shape called once or twice: none is a cold start, because there is no baseline" | RUN | **Twice is enough** (F1, Q1). Once is not: there is nothing to compare with |
| C10 | `durationSeconds` should stay wall time | READ | **Agreed.** It is what CTRF, OTLP, `Failures.md` and the history ledger (`HistoryRunBuilder.cs:84`) report, as the record of what happened |
| C11 | HotChocolate's `InitializeOnStartup()` builds the schema at start-up instead of on the first request | RUN | **True, and not enough.** The first `POST /graphql` fell from 440 and 737 ms to 108 ms, still 22 times the median; with a warm-up query (`InitializeOnStartup(warmup: ...)`) to 25 ms, unmarked (4.8) |
| C12 | The code references at `7cb2ae4` | READ | `QueryCommand.Overview.cs:117` and `HistoryRunBuilder.cs:84` unchanged. In `ReportGenerator.cs` the badge is now `:2099-2108`, the timeline `:1931-1964`, the filter `:1736-1749` |
| C13 | Kronikol adds 12 to 14 ms to the first tracked HTTP call | ISSUE | Not re-measured. The rule cannot tell that cost from the app's; it is part of what a first call is charged |
| C14 | The history verdicts read a duration against the run's speed, and none fired on the six published runs | ISSUE, READ | `HistoryAnalyzer.cs:409-470` reads each duration against its run's speed. The count was not repeated |

## 2. Where it stands today

**What a duration is.** A scenario's `Duration` is whatever its adapter measured (`Scenario.cs:36`). It has an
`EndedAt` (`:73`) and no start. A call is two records, a request and its response, paired by `RequestResponseId`
(`RequestResponseLog.cs:10-116`). Its duration is the measured `DurationMs` when a capturer set one, otherwise the
response's timestamp less the request's (`ReportGenerator.cs:4523-4550`). No .NET capturer sets `DurationMs`; ingest and
the merge reader do. Every capture has carried a timestamp since 3.15.1 (`RequestResponseLogger.cs:51-54`). (READ)

**Who called.** Nothing records "the test made this call". The caller's name is configurable per handler
(`TestTrackingMessageHandlerOptions.CallerName`, default `"Caller"`, `Constants/TrackingDefaults.cs:12`). The diagram
picks its actor as the first caller that never appears as a service among the scenario's calls
(`PlantUmlCreator.cs:1297-1301`). `flow`'s nesting rule R4 (`Query/CallNesting.cs:26-81`) is the other definition, but it
is internal, typed on the query index's entries, and compiled for net10.0 only (`Kronikol.csproj:29-31`), so the report
pipeline cannot call it. (READ)

**What order.** The data file's scenarios follow features sorted by name (`ReportGenerator.cs:4614`), so "first in
the run" has to come from timestamps. (READ)

**Where a duration is shown or ranked** (READ; every site with its line is in Appendix A):

| Surface | Where | What it does with the duration |
|---|---|---|
| Duration badge | `ReportGenerator.cs:2099-2108` | Text and colour from wall time (under 2 s fast, under 5 s moderate); `data-duration-ms` |
| Scenario Timeline | `:1931-1964` | Every scenario, longest first, bar width proportional to wall time |
| P50 to P99 filter | `:1736-1749`, `report-duration-filter-function.js:1-20` | Percentiles over wall time; hides what is below the threshold |
| Feature summary table | `:1610-1619` | Sum, mean and longest per feature |
| Parameterized group | `:2925-2930` | The group's badge is the sum of its rows |
| `kronikol query summary` | `QueryCommand.Overview.cs:117-131` | **Slowest:** the top three by wall time; `slowest[]` in `--json` |
| `scenarios --slower-than`, `diff`'s Slower, `services --sort duration` | `Overview.cs:238`, `QueryCommand.Search.cs:701-704`, `Overview.cs:289-375` | Filters and comparisons on wall time |
| `flow`, `interactions` | `QueryCommand.Narrative.cs:559-567`, `QueryCommand.Payloads.cs:119-120` | Each call's duration |
| `Failures.md`, CTRF, OTLP, the history ledger | `FailuresDigestGenerator.cs:275`, `CtrfReportGenerator.cs:189`, `HistoryRunBuilder.cs:84` | Wall time, as the record of what happened |
| Agent text | `Reports/agent-instructions.md:112`, `SKILL.md:115` (both copies) | "why is it slow?" points at `services --sort duration`, then `flow` |

**Where a run-wide pass can go.** An in-process run and `kronikol ingest` both reach
`CreateStandardReportsWithDiagramsCore`, whose every later read of the run's calls goes through one snapshot, taken by
`BackgroundAttribution.Expire` (`ReportGenerator.cs:241-246`). `kronikol merge` never enters it: it reads the shards' data
files, concatenates their calls, and keeps nothing that says which shard a call came from
(`Merge/MergeableReportMerger.cs:59`, `Merge/MergeableReport.cs:14-104`). It carries what the shards computed
(attribution, step paths, durations) rather than recomputing it. (READ)

## 3. Findings the issue does not state

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **Two later calls are one too many.** `GET /customer-preferences/{id}` is called twice per run, so the issue's rule has no baseline for it. In ReqNRoll's 2026-10-05 run its first call took 581 ms and its second 5.5 ms, and *Retrieving non-existent customer preferences should return not found* read 596 ms where the other frameworks read 2 to 47. Over the ledger it is slow in both parallel lanes (medians 778 and 628 ms). With one later call accepted, the rule explains 23 in-memory anomalies instead of 15 and still over-subtracts none (`results/truth-in-memory.txt`, V3) | RUN | 4.1, Q1 |
| F2 | **The waiter's ratio test fails where one path does unequal work.** Every GraphQL operation is `POST /graphql`, so in ReqNRoll's 2026-10-05 run its warm calls range from 15 to 126 ms with a median of 43. A call that took 405.5 ms and ended 0.7 ms after the run's first `POST /graphql` (980.8 ms) is 9.4 times the median, so the issue's rule leaves it unmarked. Requests queued behind one warm-up are released together, which a second test catches. In the local ReqNRoll runs it found waiters of 306 and 231 ms whose 10-times bar was 709 ms | RUN | 4.1, Q2 |
| F3 | **The rule explains about two in five slow outliers.** In the five in-memory lanes, 57 scenarios ran at least 3 times and 100 ms slower than their median elsewhere; the rule explains 25. The rest: paths called once (`GET /graphql/schema.graphql` 1,168 ms, `GET /openapi/v1.json` 120 to 681 ms), event-driven waits (*Consuming an order served event should trigger downstream processing* 741 ms against 27 elsewhere), own-host classes warming a second host (`GET /milk` 58 to 75 ms against medians of 3.0 to 4.6 ms in the telemetry and header-propagation classes), and a second call slower than the first (`POST /pancakes` in ReqNRoll: 115 ms, marked, then 738 ms starting 0.1 s after it ended) (`results/misses.txt`) | RUN | 4.6 |
| F4 | **It is precise in memory.** No variant at 10 times and 50 ms took more from an in-memory scenario than the other frameworks say it should (0 of 57). In the docker and external-SUT lanes P does it 4 and 5 times. Seven of the nine are the two scenarios that poll for an event (*Batch completions...* via Pub/Sub, *Equipment alerts...* via Event Hub), whose reference is itself a wait (133 to 2,072 ms). The other two are a reservation cancel (42 ms left against a reference of 118) and a preferences read (9 against 61), in lanes whose warm calls are slower and vary more (`results/truth-docker.txt`, `truth-external-sut.txt`) | RUN | 4.2 |
| F5 | **A warm-up can come in layers.** In a local ReqNRoll run the first `POST /feedback` was a validation 400 (246 ms, marked). The first that succeeded then took 3,254 ms opening the Spanner path, and was unmarked because it was the shape's second call. Judging the first call of each status class as well (`PL`) catches it: 27 explained instead of 25, 28 locally swinging scenarios instead of 30, but 7 over-subtractions in the external-SUT lanes instead of 5 | RUN | Q10 |
| F6 | **"Cold start" is taken.** `HistoryVerdicts.ColdStart` is public and means the stream has fewer runs than the verdicts need (`History/HistoryVerdicts.cs:377-381`). `query history --json` writes `coldStart` (`QueryCommand.History.cs:821`), the labs page prints its message (`HistoryHtml.cs:150`), and `commands.md` also says a partial run "pays the cold start" in the app's sense. A `coldStartMs` in `TestRunReport.json` would make `coldStart` mean two things in two `--json` outputs of one tool | READ | 4.7, Q3 |
| F7 | **BDDfy's timer (#122) puts more warm-up in a scenario than its duration.** All 31 scenarios of the 18 lanes whose warm-up calls outlast their recorded duration are BDDfy's (13 in memory, 12 docker, 6 external SUT; 16.2 s of warm-up beyond the durations recorded). *Invalid status transition should return conflict* records 43 ms and holds 1,850 ms of warm-up calls. No other adapter has one | RUN | 4.1 step 7, section 7 |
| F8 | **A merge has to carry the marks, not find them.** A warm-up belongs to one process. After a merge the calls carry no shard identity, and one merge input may itself be a merge (`MergeableReportRenderer.cs:160-167`). Recomputed over a merge, the rule would mark only each shape's globally first call | READ | 4.3 |
| F9 | **"The test's own call" needs a definition the report can compute.** R4 cannot be called from the report pipeline (section 2). The diagram's actor rule can. It agrees with `callerName == "Caller"` on every lane except in one scenario per lane, *Outbox message should transition to failed after exhausting retries*, where the test drives the app's own Cosmos client from the test thread and the diagram draws the app as the actor (7 calls in memory, all under 4 ms; 2 to 7 in docker, up to 47 ms) | READ, RUN | 4.1 step 1 |
| F10 | **Report order is not run order.** Features are sorted by name in the data file (`ReportGenerator.cs:4614`), and an ingested record without a timestamp is stamped with the replay time (`RequestResponseLogger.cs:51-54`) | READ | 4.1 step 3 |
| F11 | **A new key on every record changes every report.** An interaction is written as an anonymous type, every key and every null included (`ReportGenerator.cs:4886-4914`), and `ReportJsonEncodingTests` pins a whole report byte for byte (`TestData/Reports/TestRunReport.pin.json`). A scenario is a dictionary that already adds `diagrams` and `httpInteractions` only when it has them (`:4656-4668`) | READ | 4.4 |
| F12 | **Durations depend on the machine's culture.** On de-DE and fr-FR: XML and YAML `DurationSeconds` read `1,234` (`ReportGenerator.cs:5059`, `:5205`, `:5346`, `:5492`), which an XML or YAML reader does not take as a number. The timeline bar is `width:12,3%` (`:1948`), which a browser drops, leaving every bar at its 2 px `min-width` (`stylesheets.css:1307-1313`). `kronikol query` prints `1,27 s` (`QueryWriter.cs:510`) and Slowest `1,23s` (`QueryCommand.Overview.cs:123`). Nothing in `src/Kronikol` sets an invariant culture (`results/culture.txt`) | RUN, READ | R0 |
| F13 | **Ingest of several workers keeps no origin.** Several capture or messages files make one run, and nothing says which process served a call (`NdjsonInteractionReader.cs:50-56`). The rule then marks each shape's globally first call only: under-detection, never a false mark | READ | 4.6 |
| F14 | **The evidence for this is perishable.** BreakfastProvider's CI deletes each lane's report artifact once Pages is deployed, and Pages is overwritten nightly, so the call-level data of the issue's run (2026-09-30) is gone. The harness keeps a reduced copy of the 2026-10-05 run | RUN | harness |
| F15 | **The remedy the issue suggests is half of one.** `InitializeOnStartup()` alone leaves a 108 ms first request; with a warm-up query it is 25 ms (4.8) | RUN | 4.8 |
| F16 | **A first call can be slow by design.** A test that drives a timeout or a retry (a 503 after seconds) may happen to make its path's first call, and the rule would read it as a warm-up. BreakfastProvider's fakes fail fast (its 5xx calls take 2 to 26 ms when warm), so the corpus cannot show the case. Judging a 5xx first call only against later 5xx calls guards it, and measured identical to the unguarded rule on every lane | INFERRED, RUN | 4.1 step 5, Q11 |

## 4. The design

### 4.1 The rule

The pass runs once per run, over the calls the run's scenarios hold after `BackgroundAttribution.Expire`.

1. **The calls it judges.** A request paired with its response by `RequestResponseId` (markers and `TrackingIgnore`
   records excluded), in a scenario, whose caller never appears as a service among that scenario's calls (the diagram's
   actor rule, `PlantUmlCreator.cs:1297-1301`), and which is not an event (`MetaType.Event`). Calls in the background (no
   scenario, or moved there by `Expire`) are not judged and charge no scenario.
2. **Its shape.** The service, the method, and `InteractionShape.Template` of the path: the URI's `AbsolutePath`, or
   for a relative URI the original string cut at `?` or `#`, so the query string never splits a shape. A
   statement-shaped dependency (`DependencyCategories.IsStatementShaped`) adds its templated statement head, as
   `InteractionShape.Target` does (`InteractionShape.cs:219-230`). The consumer's `HistoryShapeTemplates` apply when set
   (Q12). Every `POST /graphql` is one shape on purpose: the schema is built once per endpoint, whatever the operation.
3. **Its order.** By the request's timestamp, ties in capture order. A shape any of whose calls lacks a start or a
   duration is not judged, because the call without one may have been first. A duration is the one the data files
   write (`ComputeInteractionDurations`, `ReportGenerator.cs:4523-4550`).
4. **The baseline.** The shape's calls that start at or after the first call ends. At least one.
5. **The first call.** A warm-up when it took at least 10 times the baseline's median and at least 50 ms more than it.
   When the first call's response is a 5xx, the baseline is the shape's later 5xx calls only (F16).
6. **The calls that waited.** A call of the shape that started while the first call ran is a warm-up too when it is at
   least 10 times and 50 ms above the baseline's median, or when it ended within 50 ms of the first call's end and is
   at least 50 ms above the median.
7. **A scenario's warm-up.** The time its warm-up calls cover, each instant counted once (calls in one scenario can
   overlap), and never more than the scenario's recorded duration. The cap only bites where an adapter's timer is short
   (F7). The tooltip and `flow` still show each call's own duration.
8. **One process at a time.** The pass sees one run's calls. A merge carries each shard's marks and never runs it
   (4.3).

Constants, not options (Q9): 10 times, 50 ms, one baseline call, 50 ms of release.

### 4.2 Why these numbers

All RUN, over the 2026-10-05 run's in-memory lanes, BDDfy left out because of #122. The ground truth: the same scenario
under the other frameworks, in the same CI run against the same app. A scenario is an **anomaly** in a lane when its
wall time is at least 3 times and 100 ms above its median in the lanes where it carries no warm-up; a rule **explains**
it when wall time less the warm-up lands within 50 ms (or 1 times) of that median, and **over-subtracts** when it lands
well below it (`warmup.py truth`).

| Variant | What it changes | Anomalies explained | Over-subtracted | Local ReqNRoll: scenarios whose time left moves 200 ms or more (of 166) |
|---|---|---|---|---|
| V0 | #113's rule as written | 15 of 57 | 0 | 39 |
| V3 | one baseline call, from calls after the first ends | 23 of 57 | 0 | 31 |
| **P** | **V3, plus released-together waiters and the 5xx guard: this plan** | **25 of 57** | **0** | **30** |
| P5 | P at 5 times and 25 ms | 26 of 54 | 1 | not run |
| P20 | P at 20 times and 100 ms | 15 of 56 | 0 | not run |
| PS | P with the status class in the shape | 19 of 57 | 0 | 30 |
| PL | P, and the first call of each status class judged too | 27 of 58 | 0 | 28 |

The count of anomalies moves with the variant because a scenario's reference is its median in the lanes where that
variant marks nothing.

- **The floor and the ratio.** At 5 times and 25 ms the rule adds 6 to 10 calls per in-memory lane, of 29 to 138 ms:
  mostly too small to move a ranking, and they are where the first over-subtraction appears. At 20 times and 100 ms it explains 10 fewer
  in memory, and in the docker lanes it drops large first calls whose warm medians are higher (`POST /pancakes`
  1,435 ms against 78 ms, 18.4 times, in the docker ReqNRoll lane) (`results/variants.txt`, `results/truth-*.txt`).
- **The measure.** Over the 211 warm-up calls of the 18 lanes, the time they cover (95.4 s) and the time above their
  baselines (92.5 s) differ by 3.1%, so the simpler measure, the calls' own time, is used (Q4).
- **The serial lanes are stable already.** Two local xUnit runs mark the same nine calls in the same scenarios; the
  scenarios whose time moves by 200 ms or more fall from 6 to 3 of 212.

The issue's own first table, from the same CI run (wall ms, then the time left where the scenario carries a warm-up):

| Scenario | xUnit | NUnit | TUnit | ReqNRoll | LightBDD | BDDfy |
|---|---|---|---|---|---|---|
| Order status via grpc should return order details | 1,265 / 76 | 38 | 23 | 73 | 82 | 43 |
| Equipment alerts should contain data ingested via event hub consumer | 791 / 45 | 45 | 21 | 458 / 52 | 105 | 34 |
| Audit logs should be filterable by entity id | 32 | 1,012 / 71 | 593 / 57 | 90 | 79 | 5 |
| Audit logs should be returned in descending timestamp order | 48 | 42 | 25 | 55 | 95 | 13 |
| Batch completions should contain data ingested via pubsub consumer | 41 | 843 / 34 | 463 / 12 | 65 | 1,334 / 97 | 26 |
| Retrieving non-existent customer preferences should return not found | 23 | 10 | 4 | 596 / 16 | 47 | 2 |

Every cell reads 2 to 105 ms once the warm-up is out. Under the issue's rule the two ReqNRoll cells stay at 458 and
596 (`results/table-V0.txt`). The six columns stay apart in absolute terms (BDDfy's column is low throughout, #122), which
is why the acceptance compares a scenario with itself across frameworks rather than columns with each other.

A second app: the eleven `examples/Example.Api` suites (2 to 12 scenarios each). The run's first call, `GET /milk` at
106 to 358 ms against a later median of 2.5 to 9.5 ms, is marked in 6 of the 10 lanes that made calls; the only other mark
is a second `GET /milk` in ReqNRoll.xUnit3 that waited for the first. Of the four unmarked lanes, three ran their
`GET /milk` calls at once, so no call started after the first ended, and in the fourth the first call (131 ms) was 7
times the later median: under-detection, not a false mark (`results/marks-example-api.txt`).

### 4.3 Where it runs, and what carries it

- **In-process runs and ingest.** A new internal `WarmUpCalls` (`src/Kronikol/Reports/`), called in
  `CreateStandardReportsWithDiagramsCore` right after `BackgroundAttribution.Summarise` (`ReportGenerator.cs:246`), over
  `runLogs`. It returns two side tables: request `RequestResponseId` to its mark (kind, shape, baseline median, baseline
  count, and for a waiter the first call's id), and scenario id to its warm-up. They travel to the writers the way
  durations and step paths do (`GenerateTestRunReportData` and its callers). `RequestResponseLog` and `Scenario` gain
  nothing: the mark is a reading the report takes, like `stepPath`, so the capture types and
  `RequestResponseLogRoundTripTests` are untouched.
- **`kronikol ingest`** reaches the same core (`IngestPipeline.cs:488-491`), so it gets the pass with no code of its own.
  Its records are replayed in call-tree order per test, not in time (`IngestPipeline.cs:1043-1060`), which is why step 3
  orders by timestamp.
- **`kronikol merge`** reads `warmUp` and `warmUpSeconds` back in `MergeableReportReader.ReadInteractions` and
  `ReadScenario` (`MergeableReportReader.cs:479-547`, `:208-238`) into side tables on `MergeableReport` beside
  `StepPaths`, carries them through `MergeableReportMerger.Merge` (renamed ids included), and hands them to `Render` and
  `Serialize`. It never runs the pass. Shards written before R1 carry no marks and get none.
- **Retries.** An in-process framework retry folds every attempt's calls into one scenario (`DistinctBy` in each
  adapter), so a retried scenario holding the run's first call keeps its mark. Ingest keeps only the last attempt's
  calls (`IngestPipeline.cs:767-792`), so a warm-up paid by a dropped attempt is not seen. A process-level re-run is a
  new process and warms up again, and is marked again. (READ)

### 4.4 What it records

Only where there is something to record (F11), so a run with no warm-up writes the bytes it writes today and
`TestRunReport.pin.json` holds.

**`TestRunReport.json` and the mergeable file** (both are written by `BuildFeaturesJsonModel` and `MapLogJson`):

```json
{ "name": "Order status via grpc should return order details", "durationSeconds": 1.265, "warmUpSeconds": 1.189 }
```

```json
{ "type": "Request", "method": "POST", "uri": "http://localhost/orders", "durationMs": 661.2,
  "warmUp": { "kind": "first", "shape": "POST /orders", "baselineMs": 7.6, "baselineCalls": 39 } }
{ "type": "Request", "method": "POST", "uri": "http://localhost/graphql", "durationMs": 405.5,
  "warmUp": { "kind": "waited", "shape": "POST /graphql", "baselineMs": 43.0, "baselineCalls": 5,
              "first": "<requestResponseId of the run's first POST /graphql>" } }
```

- `warmUpSeconds` sits beside `durationSeconds`, in seconds like it, so a reader subtracts without converting (Q3).
- `warmUp` is on the request record only, the half that starts the call. `shape` is the method and the templated path;
  the service is the record's own `serviceName`.
- **YAML:** `WarmUpSeconds` and a `WarmUp` mapping, written only when present, as `MetaType` and `Phase` already are
  (`ReportGenerator.cs:5521-5577`). **XML:** optional elements at the end of their sequences, so documents written
  before R1 still validate against the new XSD (`:5138`, `:6653`). Every new number is written with the invariant
  culture.
- **Schemas** (generated, `ReportGenerator.cs:6059-6582`): `warmUpSeconds` (number, at least 0) on the scenario and a
  closed `warmUp` object (`kind` one of `first` and `waited`) in `$defs.httpInteraction`, each with a description; the
  YAML schema in PascalCase; the XSD. `TestRunReportSchemaContractTests` and `SchemaClosedContractTests` hold them.
- **The query engine** (`Query/ReportScanner.cs:697-794`, `:841-873`) reads both into `ScenarioEntry` and
  `InteractionEntry`. `query.py` prints no durations and is unchanged.

### 4.5 What each surface shows

**The CLI (R1).**

- `summary`. Slowest ranks by the time left and says so; a line under it says where the warm-up went. On the
  2026-10-05 xUnit report:

  ```text
  Slowest, first-call warm-up left out:
    s100  1.23s  Valid order should be created and an event published
    s109  1.13s  Outbox message should transition to failed after exhausting retries
    s101  0.94s  Creating an order should produce an audit log entry and events
  First-call warm-up: 2.65s in 7 scenarios; the most in s49, 1.19s of its 1.27s (flow s49)
  ```

  Today the list opens with s49 at 1.27 s, three first calls. `--json`: each `slowest[]` item gains `warmUpSeconds` when
  it has one, ordered by `durationSeconds - warmUpSeconds`, and a `warmUp` member (`seconds`, `scenarios`, `calls`, the
  largest) appears when the run has any. `Summary_stays_small` (under 2,000 bytes) still holds.
- `flow`. A marked call's line gains one field after its duration, before the body hash, so neither the start nor the
  `inside sN/iM` ending that `FlowTreeTests` reads moves (`QueryCommand.Narrative.cs:559-567`):
  `661 ms  first POST /orders of the run: later calls 7.6 ms median (39)`, or for a waiter
  `406 ms  waited for s88/i1, the run's first POST /graphql: later calls 43 ms median (5)`.
- `interactions`. A marked row gains ` warm-up` after the duration column, so the address stays first and the
  response's `b:` hash stays the last such token (`QueryCommandTests` row parsers). `--json` items gain `warmUp`.
- Agent text. The "why is it slow?" row in `Reports/agent-instructions.md:112` and in both `SKILL.md` copies (`:115`)
  starts at `summary`, which now separates the warm-up, and says the rest of the recipe is unchanged; `commands.md`
  (both copies) documents the Slowest heading, the warm-up line, the `flow` field and the `warmUp` members.
  `SkillDriftTests` holds the copies identical.

**The HTML (R2).**

- **The badge** keeps wall time as its text. A marked scenario gets `data-warmup-ms`, a marker (Q5: three looks, one
  recommended), and a tooltip naming the calls: `1.27 s, 1.19 s of it first-call warm-up: POST /orders 661 ms (later
  calls 7.6 ms), POST /pancakes 446 ms (5.2 ms), GET /milk 82 ms (2.3 ms)`. Its colour class reads the time left, so a
  scenario that is slow only because it went first is not coloured slow.
- **The Scenario Timeline** keeps its order (it is a picture of elapsed time) and shades the warm-up's share of a bar,
  with the same tooltip.
- **The P50 to P99 filter** computes its percentiles over the time left and filters on it (`data-duration-ms` less
  `data-warmup-ms`); the filter's label says so in its title. Export Filtered CSV still writes wall time.
- **A parameterized group** sums its rows' warm-up as it sums their durations.
- **The rules** go in a new `warm-up-styles.css`, appended only to a page that draws a mark, as 4.6.0 did for history
  (`history-styles.css`). `stylesheets.css` is byte-shared with Kronikol4J and stays as it is. The diagram file-name
  builder strips the marker with the badge (`context-menu-script.js:25`).
- **`Specifications.html`** draws badges, the filter and the timeline from the same code (`ReportGenerator.cs:485`), so
  it shows the same marks.

### 4.6 What it cannot see

| Kind | Example (RUN) | Why not | What the docs say |
|---|---|---|---|
| A path called once | `GET /graphql/schema.graphql` 1,168 ms (ReqNRoll); `GET /openapi/v1.json` 120 to 681 ms | Nothing to compare it with | That a single call's warm-up cannot be told from its work, and how to warm the path (4.8) |
| A first call whose later calls all overlapped it | Three Example.Api lanes (TUnit, ReqNRoll.TUnit, LightBDD.TUnit), `GET /milk` 110 to 157 ms | No call ran warm after it | Nothing to add: it is rare outside tiny suites |
| An event-driven wait | *Consuming an order served event should trigger downstream processing* 741 ms against 27 elsewhere | The warm-up is in the app's consumer; the test's own calls are not slow | That polling scenarios carry the consumer's first message, and to warm it in a fixture |
| A second host | `GET /milk` 58 to 75 ms against medians of 3.0 to 4.6 in the classes that build their own host | Only the run's first call of a shape is judged; nothing records which host served a call | That a test which builds its own host pays part of the warm-up again |
| A second call slower than the first | `POST /pancakes` in ReqNRoll: 115 ms (marked), then 738 ms starting 0.1 s after it | Only a call that overlaps the first is judged a waiter | That parallel lanes spread warm-up over more than one call |
| A first success after a first failure | `POST /feedback` 3,254 ms after a 246 ms 400 (local ReqNRoll) | The shape's second call | Q10 |
| Several processes in one ingest | F13 | No origin on a record | That the rule marks each shape's first call across the files |

### 4.7 Vocabulary

*First-call warm-up* in prose and UI text, *warm-up* for short; `warmUp` and `warmUpSeconds` in data. Never "cold
start", which history owns (F6). `Cross-Run-History.md` gains one sentence at its "cold start" (`:358`): the history
term is a short ledger, and the app's first-request cost is what the report calls warm-up.

### 4.8 How to avoid it

Measured on BreakfastProvider's xUnit lane (local, Windows; `results/marks-local.txt`):

| Run | The run's first `POST /graphql` | Marked | *Order summaries should return an empty list...* |
|---|---|---|---|
| As it is, twice | 440, 737 ms (later median 7.0, 5.4) | yes | 446, 743 ms |
| `InitializeOnStartup()` | 108 ms (4.8) | yes, 22 times | 114 ms |
| `InitializeOnStartup(warmup: ...)` running that scenario's own query | 25 ms (4.7) | no | 30 ms |

The wiki section (4.8 of the docs, R1) says:

- Send one request down each expensive path before the first scenario, from an assembly or collection fixture, through
  a client Kronikol does not track (or a tracked one outside any test, whose calls land in no scenario).
- For HotChocolate, `InitializeOnStartup(warmup: (executor, ct) => ...)` with a real query; the schema alone leaves a
  100 ms first execution (`breakfastprovider/warmup-query.patch`). It runs as a hosted background service, so a suite
  whose first scenario is a GraphQL one can still wait on it.
- The trade: a warm-up hides the first-request cost production users pay after a deploy. If that matters, test it on
  purpose, in a scenario of its own, and let the mark show it.

## 5. Tests, red first

Every fact is red on 4.6.0 for its own reason; the run that proves it is kept in the release's log. New classes:
`Reports/WarmUpCallsTests.cs` (the rule), `Reports/WarmUpDataTests.cs` (the records), `Tool/WarmUpQueryTests.cs` (the
CLI), `Reports/DurationCultureTests.cs` (R0), and in `Kronikol.Tests.EndToEnd`, `WarmUpReportTests.cs` and a culture
fact in the timeline's class.

| # | Fact | Red on 4.6.0 because | Slice |
|---|---|---|---|
| T1 | A shape called five times, the first at 600 ms and the rest at 5: the first is marked `first`, its scenario's `warmUpSeconds` is 0.6 (#113) | no pass | S2 |
| T2 | Five calls of 600 ms: none marked (#113) | no pass (vacuous on the old code; kept, and proved by mutation) | S2 |
| T3 | One call of a shape: none marked. Two (600, 5): the first is marked (F1) | no pass | S2 |
| T4 | The app's calls inside a marked test call are not judged, and the scenario's warm-up is the test call's time alone (#113) | no pass | S2 |
| T5 | `/orders/{guid}` and `/orders/{another guid}`, `/orders?page=1` and `?page=2`, and `/recipe/Recipe-<32 hex>` and another are one shape each (#113) | no pass | S2 |
| T6 | A scenario that polls, every call warm, has no warm-up (#113) | no pass | S2 |
| T7 | A call that overlaps the first and is 10 times the median is `waited`, with `first` set | no pass | S2 |
| T8 | A call that overlaps the first, is 9.4 times the median and ends within 50 ms of it is `waited` (F2) | no pass | S2 |
| T9 | A call that overlaps the first, is under 10 times and ends 200 ms before it is not marked | no pass (and the release mutation) | S2 |
| T10 | A slow call that starts after the first ended is not marked | no pass | S2 |
| T11 | A shape with one untimed call is not judged | no pass | S2 |
| T12 | Two marked calls overlapping in one scenario are counted once; a warm-up longer than its scenario is capped at the scenario's duration | no pass | S2 |
| T13 | Exactly 10 times and 50 ms is marked; 9.99 times is not; 49.9 ms above is not | no pass | S2 |
| T14 | A test caller renamed `"Client"` is judged; a caller that also appears as a service in its scenario is not; an event is not | no pass | S2 |
| T15 | A call `Expire` moved to the background is neither judged nor charged | no pass | S2 |
| T16 | Two statements on one statement-shaped connection with different heads are two shapes | no pass | S2 |
| T17 | The data file's order is not the run's: a later feature by name that ran first holds the mark | no pass | S2 |
| T18 | A 5xx first call with later 2xx calls only is not judged; with a later fast 5xx call it is (F16) | no pass | S2 |
| T19 | Over two reduced corpus lanes copied from the harness (xUnit and ReqNRoll of 2026-10-05), the pass marks exactly what `warmup.py marks` printed | no pass | S2 |
| T20 | A marked request carries `warmUp`, an unmarked one no key; a scenario carries `warmUpSeconds` only when it has a warm-up; `TestRunReport.pin.json` still matches | no field | S3 |
| T21 | Every new key is declared and described; the closed schema validates a marked report; YAML and XML carry the same marks (`DataFormatParityTests`); a pre-R1 XML document validates against the new XSD | undeclared | S3 |
| T22 | Merged: two shards, each with its own first call of one shape, keep both marks; a merge of shards with no marks has none, though the merged calls would trigger the rule | marks lost | S3 |
| T23 | `kronikol ingest` of an NDJSON feed with a warm-up marks it | no pass | S3 |
| T24 | Under de-DE, the new numbers in XML and YAML parse invariantly | no field | S3 |
| T30 | `summary`: Slowest by time left with its heading, the warm-up line, `slowest[]` with `warmUpSeconds` and the `warmUp` member; under 2,000 bytes | wall order | S4 |
| T31 | `flow`: the field after the duration on a first and on a waited call; `FlowTreeTests` and the `inside` pattern still hold; no line ends in a space | no field | S4 |
| T32 | `interactions`: ` warm-up` after the duration, the `b:` hash still last, `warmUp` in `--json` | no field | S4 |
| T33 | A report written before R1 reads as it did, output byte for byte | (holds on 4.6.0; kept as a guard) | S4 |
| T34 | `query.cs` prints what the tool prints for the new lines (`QueryScriptEndToEndTests`) | no field | S4 |
| T40 | Playwright: the marker and its tooltip appear on every marked scenario and on no other (#113); its colour class follows the time left | no marker | S5 |
| T41 | Playwright: the timeline's shaded share is the warm-up's share of the bar's painted width, within 1 px (#113) | no shading | S5 |
| T42 | Playwright: at P90, a scenario above P90 by wall time and below it by time left is hidden | wall time | S5 |
| T43 | Playwright: `Specifications.html` shows the same mark; Export Filtered CSV still writes wall time; Export Filtered HTML keeps the marker and its tooltip | no marker | S5 |
| T44 | A page without a mark carries no `warm-up-styles.css` rules and is byte-identical to one written without the pass | (holds; guard) | S5 |
| T50 | Under de-DE and fr-FR, XML and YAML `DurationSeconds` and step durations read `1.234` (F12) | `1,234` | S1 |
| T51 | Under de-DE, the timeline bar's style is `width:12.3%`, and in Playwright a report generated under de-DE paints bars of different widths | every bar 2 px | S1 |
| T52 | Under de-DE, `kronikol query` durations and Slowest read with a point | `1,27 s` | S1 |

Mutations, each run once and each caught by the fact named:

| Mutation | Caught by |
|---|---|
| Two baseline calls instead of one | T3 |
| The baseline taken from all later calls, overlapping ones included | T8 |
| No released-together test | T8 |
| The 5xx guard removed | T18 |
| Ratio 5 or floor 25 | T13 |
| File order instead of timestamps | T17 |
| The app's calls judged too | T4, T14 |
| A merge recomputes | T22 |
| `warmUp: null` written on every record | T20 |
| The filter reads wall time | T42 |
| The marker drawn on every badge | T40 |
| `F1` formatted with the current culture again | T51 |

## 6. Slices, releases and records

### 6.1 Slices

| Slice | What | Bump |
|---|---|---|
| **S1** | F12: invariant XML and YAML durations (four sites), the timeline's width, `QueryWriter.Duration` and Slowest; T50 to T52 | patch |
| **S2** | `WarmUpCalls`: the rule of 4.1, the actor test, the shape, the side tables; T1 to T19 | (R1) |
| **S3** | The records: JSON, YAML, XML, both schemas, the XSD, the mergeable reader and writer, ingest; T20 to T24 | (R1) |
| **S4** | The CLI: the scanner, `summary`, `flow`, `interactions`, `--json`; the agent text; T30 to T34 | (R1) |
| **S5** | The HTML: the badge (Q5), the timeline, the filter, `warm-up-styles.css`, `Specifications.html`; T40 to T44 | (R2) |
| **S6** | Docs for each release: the wiki (6.4), the changelog, doc comments (the `HistoryShapeTemplates` doc names the warm-up if Q12 is taken) | with each |
| **S7** | The consumer acceptance (6.6) | with each |

### 6.2 Releases

| Release | Contents | Bump | Why this part moves |
|---|---|---|---|
| **R0** | S1, S6 for it | patch | Bug fixes; nothing new to call. The changelog names the visible change: durations read with a point on every machine |
| **R1** | S2 to S4, S6 and S7 for them | minor | `warmUpSeconds` and `warmUp` are new fields in a published format and its schema, and the CLI shows new lines. `CLAUDE.md`: anything new is a minor, and the highest-ranking change decides |
| **R2** | S5, S6 and S7 for it | minor | New report UI |

R0 can ship at once and alone (rule 1). R1 and R2 can be one minor if Q5 is answered before S5 starts (Q13). A version
number is taken when the release is cut; until then this plan says R0, R1, R2.

### 6.3 Changelog drafts

> **Patch: durations read the same on every machine.** On a machine whose culture writes a decimal comma, XML and YAML
> wrote `DurationSeconds` as `1,234`, which their readers do not take as a number; the Scenario Timeline wrote each
> bar's width as `12,3%`, which a browser drops, so every bar was drawn 2 px wide; and `kronikol query` printed `1,27 s`.
> All of them now use a point. The patch part moved because nothing is new for a consumer to call.

> **Minor: a scenario's duration says how much of it was the app's first-call warm-up (#113).** The first request down
> each path can cost tens of times what later ones do (JIT, a schema build, a first connection), and whichever scenario
> goes first pays it. The report now finds those calls: for each kind of call a test makes (service, method and path,
> ids folded), the run's first is marked when it took at least 10 times and 50 ms more than the median of the later
> ones, and so is a call that waited for it. `TestRunReport.json` (and YAML, XML, both schemas and the mergeable file)
> gains `warmUpSeconds` on a scenario that carries such calls and `warmUp` on each of them; `durationSeconds` is
> unchanged, and a run with no warm-up writes the same bytes as before. `kronikol query summary` ranks Slowest without
> the warm-up and says where it went; `flow` and `interactions` mark the calls. A merged report keeps each shard's marks.
> The minor part moved because the fields and the CLI's lines are new.

> **Minor: the report shows the first-call warm-up.** A scenario that carries one has a marker on its duration badge
> whose tooltip names the calls, its colour follows the time left, the Scenario Timeline shades the warm-up's share of
> its bar, and the P50 to P99 filter ranks without it. The minor part moved because the report's UI is new.

### 6.4 Wiki

The checkout at `../Kronikol.wiki` is at `e164dbc` (4.5.0), behind origin: pull first, edit in a worktree of its own.

| Page | Change | Release |
|---|---|---|
| `Generated-Reports.md`, the interaction fields (`:268`) and the scenario fields | `warmUp`, `warmUpSeconds`, with the rule in two sentences | R1 |
| `Generated-Reports.md`, Report Features (`:482-500`) | The badge, the timeline and the P50 to P99 filter, none of which is documented today, with the warm-up marker | R2 |
| `Querying-Reports.md`, `#### summary` (`:378-394`) | A sample with the Slowest section (it has none today) and the warm-up line; the `slowest` and `warmUp` JSON members (`:351-360`); `flow` and `interactions` | R1 |
| A new section, "Why is my first scenario slow?" (in `FAQ.md` or a page of its own, with `_Sidebar.md`) | 4.6 and 4.8, measured | R1 |
| `Cross-Run-History.md` (`:358`, `:379`) | The vocabulary sentence of 4.7 | R1 |
| `Merging-Parallel-Reports.md` | Marks are per shard, carried, never recomputed | R1 |
| `Ingesting-External-Captures.md` | Ingested runs are marked; several workers in one ingest are under-marked (F13) | R1 |
| `Integration-BDDfy-xUnit3.md` | Until #122 is fixed, a BDDfy scenario's warm-up can exceed its recorded duration and is capped | R1 |

### 6.5 Kronikol4J

The port pins .NET 3.0.43 HTML and 3.0.47 data (`GoldenHtmlParityTest.java:63`, `ReportDataParityTest.java:36`), so no
Java test fails. Ledger lines in `docs/REMAINING_PARITY.md`, after publication:

- **R0.** Check how the port writes durations in XML and YAML (`String.format` is locale-sensitive in Java). The line
  says either that the port already writes a point or that it has the same defect.
- **R1.** Not mirrored: `warmUpSeconds`, `warmUp`, their schema and XSD entries. The port has no warm-up pass.
- **R2.** Not mirrored: the marker, the shading, the filter by time left, `warm-up-styles.css`.

### 6.6 Before declaring done

- **The consumer (S7).** In a scratch clone of BreakfastProvider (never the shared checkout), on local packages of the
  release: xUnit twice and ReqNRoll three times (`breakfastprovider/runlanes.sh`). For each report, the release's marks
  equal `warmup.py marks` over the same file call for call, and each `warmUpSeconds` equals the script's within 1 ms.
  Control: run the same comparison on a report the previous release wrote, which carries no marks. It must fail and
  list every mark the script finds; a comparison that passes there cannot catch anything. Classify every difference on
  the release's own reports before calling it a bug. The section 4.2 table, from the release's own fields, matches. Then, at the
  owner's word, the pin move and two CI runs on the consumer, and the live page of a marked scenario.
- Every fact of section 5 found in the diff, each red on 4.6.0 for its own reason, each mutation caught.
- `release.slnf` built in Release for every target (Query is net10.0 only; `WarmUpCalls` is not, and must not call it).
- The full Playwright suite, and the CI run of every pushed SHA read.
- Every doc comment that describes a duration, Slowest or the filter re-read (`grep -rn "Slowest\|percentile\|duration badge" src`).
- #113 closed with a comment naming the releases; #122 and #123 cross-referenced, not closed.
- A ROADMAP Appendix C row for what this plan leaves (Q8, Q10, Q15, section 8).
- No "fixes #N" in passing in a commit message, and no bracketed skip-ci marker.

## 7. Where it sits in the roadmap

Row **1.19** in stage 1, placed by **rule 1**: the report ranks scenarios by a number that carries the app's warm-up
and says nothing, so a reader optimises the wrong test, and a parallel lane's own history baseline is set by whichever
runs went first. R0 is a live defect in shipped code and goes first; rule 5 agrees, since R1 and R2 edit the same lines.
R1 and R2 are marked minor. **D33** is the green light and the answers to Q1 to Q15.

Rule 4 ("nothing is sized on data known to be wrong") applies to BDDfy: its durations are short until #122 is fixed, so
the acceptance leaves the BDDfy lanes out of the section 4.2 comparisons, as this plan did, and R2's consumer check waits
for #122 or says it did not.

## 8. Found on the way, not in this plan

| What | Level | Where it goes |
|---|---|---|
| F12, the culture defects | RUN | R0 |
| The percentile pick is `sorted[(int)(N * p)]`, so the P50 of two durations is the larger (`ReportGenerator.cs:1744`) | READ | A patch row; or R2, which edits the lines (Q7) |
| A parameterized group's badge and `data-duration-ms` are the sum of its rows, while the percentiles are over single scenarios, so the filter compares a sum with single values (`:2925-2930`) | READ | Q7 |
| The feature summary table sorts on `parseFloat` of its text, so `500ms` sorts above `1m 5s` (`report-sort-table-function.js:1-17`) | READ | A patch row |
| The timeline's info tooltip says yellow means skipped; skipped is grey and bypassed orange (`:1937`, `stylesheets.css:1320-1325`) | READ | R2, which rewrites the tooltip |
| `Querying-Reports.md`'s summary sample has no Slowest section; the badge, the timeline and the filter are not documented | READ | R1 and R2 docs |
| `commands.md`'s `--json` verb list omits `repro` and `history` | READ | A docs patch |
| A merge reads an unknown duration (written as 0) back as 0, so a merged report shows "0ms" badges; it also reads a derived `durationMs` back as measured (`MergeableReportReader.cs:214-216`, `:531`) | READ | A patch row |
| Merged HTML draws no Background calls section (`MergeableReportRenderer.cs:66-107`) | READ | A patch row |
| The merger's `Rename` rewrites `TestId` and not `ExpiredFromTestId`, so with colliding runtime ids a background group may attach to the wrong shard's scenario (`MergeableReportMerger.cs:297`) | READ, not run | To reproduce first |
| `Failures.md` times calls from timestamps only, ignoring a measured `DurationMs` (`FailuresDigestGenerator.cs:392-395`) | READ | A patch row |
| `RequestResponseLogger.LogPair` stamps both halves with one instant, so its calls have `durationMs` 0 (`:106-127`) | READ | A patch row |
| `services` reads durations from the response half only, and `--sort duration` means the total there and the median under `interactions --group-by` | READ | A docs or patch row |
| `KRONIKOL_SHARD` in the wiki's sharding example is read by nothing (`Merging-Parallel-Reports.md:166`) | READ | A docs patch |
| #122 (BDDfy async steps), #123 (xUnit v2 result matching) | ISSUE | Their own rows; #122 before R2's acceptance |

## 9. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | The baseline: one later call (F1) or the issue's two | **One.** It explains the issue's own parallel-lane example and 8 more anomalies, with no over-subtraction. Once is still not enough: a single call has nothing to compare with |
| Q2 | Waiters: the ratio test alone (the issue), or also "ended within 50 ms of the first" (F2) | **Both.** It catches the waiter class the issue describes and costs nothing measured |
| Q3 | The names and the unit: `warmUpSeconds` and `warmUp`, or the issue's `coldStartMs` | **`warmUpSeconds` and `warmUp`.** "Cold start" means a short ledger in history's public API and `--json` (F6), and seconds match `durationSeconds` beside it |
| Q4 | What `warmUpSeconds` measures: the time the calls cover (the issue's 600 for a 600 ms call) or their time above the baseline | **The time they cover.** The two differ by 3.1% over 211 calls, and a call's own duration is what a reader can check |
| Q5 | The badge's marker. **A:** text in the badge, muted, `1.27s · 1.19s warm-up`. **B:** a mark after the time, `1.27s*`, the rest in the tooltip. **C:** the warm-up's share hatched inside the badge. All three keep wall time as the badge's text and take the colour from the time left | **A.** It reads without a hover, which phones lack, and appears on few scenarios (7 of 212 in the xUnit lane, 17 of 214 in ReqNRoll). The owner's call: the default report was cleared of history's sparkline and pill for being noisy, and this is the same row of the header |
| Q6 | The timeline: shade only, or also reorder by time left | **Shade only**, as the issue asks. The timeline is a picture of elapsed time; the filter and Slowest are the rankings |
| Q7 | The duration surfaces the issue does not name | **`scenarios --slower-than` and `diff`'s Slower read the time left** (the same misreading; `diff` only where both runs carry the field). A parameterized group sums its rows' warm-up. **Unchanged:** the feature summary table, `Failures.md`, CTRF, OTLP and the ledger, which report what happened. The percentile pick (section 8) is fixed in R2 if it is taken |
| Q8 | History (#113's item 4): record the warm-up beside each duration so `slower` reads what is left | **Not in this plan.** Revisit after R1 has written ten runs on the consumer, then measure what `slower` would read. An optional per-slot array on the run line is the likely form; whether older readers skip an unknown key is unverified (A4) |
| Q9 | The thresholds as options | **No.** Constants, until a second consumer gives a reason (A1) |
| Q10 | Layered warm-ups (F5) | **Not now.** +2 explained and -2 swinging scenarios, but +2 over-subtractions in the external-SUT lanes, and it multiplies F16's risk. Revisit with a consumer whose validation and success paths differ more |
| Q11 | The 5xx guard (F16) | **Yes.** Measured free on every lane, and it guards a false mark the corpus cannot contain |
| Q12 | The consumer's `HistoryShapeTemplates` applied to warm-up shapes | **Yes, when set.** The consumer has said what an id looks like; without it their ids split shapes, which only under-marks. The option's doc comment says so |
| Q13 | R1 and R2 as one minor or two | **Two**, unless Q5 is answered before S5 starts. R1 helps every agent and script at once, and does not wait on a design choice |
| Q14 | #122's order | **Before R2's consumer acceptance**, by rule 4. R1 can ship before it: the cap of 4.1 step 7 keeps BDDfy's time left at zero, not negative |
| Q15 | A report diagnostic when a scenario's own calls outlast its recorded duration (it would have caught #122) | **Not in this plan**: a sibling found by it. It belongs with #122 |

## 10. Assumption ledger

| # | Assumption | Level | What would change |
|---|---|---|---|
| A1 | One consumer (six frameworks, three environments) and a small second app are enough to set 10 times and 50 ms | INFERRED | A consumer with slower warm calls (a remote database) could need a lower ratio; Q9 |
| A2 | In-process durations are timestamp deltas; no .NET capturer sets `DurationMs` | READ | A capturer that measured could make durations of one shape disagree in kind |
| A3 | Readers of `TestRunReport.json` ignore keys they do not know: the query scanner reads by name, the merge reader too, `query.py` reads named fields | READ | A third-party reader with a closed schema of its own breaks on R1; the changelog says the fields are new |
| A4 | The ledger reader skips an unknown key on a run line | not verified | Q8's format |
| A5 | `InitializeOnStartup`'s warm-up finishes before the first GraphQL scenario in the measured run | INFERRED | A suite whose first scenario is GraphQL may still wait on it; the docs say so |
| A6 | No test-made call in the corpus is statement-shaped | RUN | Step 2's statement head is pinned by T16 alone |
| A7 | BreakfastProvider's fakes fail fast, so F16's case never occurs in the corpus | RUN | The guard is proved by T18 alone |
| A8 | Local Windows runs show the CI mechanism, larger: 30 to 35 ReqNRoll scenarios carry a warm-up locally per run, 17 in CI | RUN | The local stability numbers are an upper bound |

## 11. Log

- **2026-10-06.** Written at 4.6.0 (`37e93813`). Read #113, #122 and #123. Four code maps (data model and pipeline,
  every duration surface, shapes and the CLI's call lines, merge, ingest and Kronikol4J) checked against the source at
  the lines cited.
  - Downloaded BreakfastProvider's 18 published reports of run `gh:37292325012:1` (2026-10-05, Kronikol 4.0.2,
    BreakfastProvider `106702b`) and its ledger (`kronikol-history` at `c2e7db61`, 969 runs). The issue's run of
    2026-09-30 could not be fetched (F14).
  - Wrote the rule as a script, tested V0, V3, PU, P, P5, P20, PS and PL against cross-framework ground truth, and
    reduced the data to the harness's corpus (1.5 MB).
  - Ran the consumer locally in a scratch clone (Windows 11, SDK 10.0.300): ReqNRoll three times, xUnit twice, and xUnit
    with `InitializeOnStartup()` and then with a warm-up query.
  - Ran the rule over the eleven `Example.Api` reports in the build output (Kronikol 3.0.77 to 3.28.0).
  - Measured F12 with `culture/culture.cs`.
  - Placed as row 1.19 and D33 in `ROADMAP.md`, after rows 1.17 (#115) and 1.18 (#105), written the same day by other
    sessions; numbers agreed with them before writing.

## Appendix A. Edit sites at `37e93813`

A list of what this plan's author found, not of what the change touches. Before declaring a slice done, grep for every
reader of `durationSeconds`, `data-duration-ms` and `slowest`, and for every writer of an interaction record.

| Slice | File | Lines | What |
|---|---|---|---|
| S1 | `src/Kronikol/Reports/ReportGenerator.cs` | 5059, 5205, 5346, 5492 | XML and YAML `DurationSeconds` |
| S1 | same | 1948 | The timeline bar's width |
| S1 | `src/Kronikol/Query/QueryWriter.cs` | 510 | `Duration` |
| S1 | `src/Kronikol/Query/QueryCommand.Overview.cs` | 123 | Slowest's seconds |
| S2 | `src/Kronikol/Reports/ReportGenerator.cs` | 241-250 | Call `WarmUpCalls` after `Summarise` |
| S2 | `src/Kronikol/History/InteractionShape.cs` | 98-122, 219-230 | `Template`; the statement head of `Target` (make it reachable, internal) |
| S2 | `src/Kronikol/PlantUml/PlantUmlCreator.cs` | 1297-1301 | The actor rule, to share rather than copy |
| S2 | `src/Kronikol/Reports/ReportGenerator.cs` | 4523-4550 | `ComputeInteractionDurations`, reused |
| S3 | same | 4359-4384, 4569-4604, 4611-4673, 4681-4746, 4762-4845, 4886-4914 | The data writers, scenario and interaction |
| S3 | same | 5138 (XML), 5521-5577 (YAML), 6059-6582 (schemas), 6653 (XSD) | The other formats |
| S3 | `src/Kronikol/Reports/Merge/MergeableReportReader.cs` | 208-238, 479-547 | Read back |
| S3 | `src/Kronikol/Reports/Merge/MergeableReport.cs`, `MergeableReportMerger.cs`, `MergeableReportRenderer.cs` | 74-80; 59-62, 288-304; 33-131, 168-198 | Carry |
| S4 | `src/Kronikol/Query/ReportScanner.cs`, `ReportIndex.cs` | 697-794, 841-873; 109-179, 210-273 | Read |
| S4 | `src/Kronikol/Query/QueryCommand.Overview.cs` | 117-131 | Slowest |
| S4 | `src/Kronikol/Query/QueryCommand.Narrative.cs` | 559-567 | `flow`'s line |
| S4 | `src/Kronikol/Query/QueryCommand.Payloads.cs` | 116-143 | `interactions`' row and JSON |
| S4 | `src/Kronikol/Reports/agent-instructions.md`; `templates/skills/kronikol-test-debugging/` and `.claude/skills/kronikol-test-debugging/` (`SKILL.md`, `references/commands.md`); `templates/agents/CLAUDE.md` | 112; 115; 27 | The recipes |
| S5 | `src/Kronikol/Reports/ReportGenerator.cs` | 1366-1367, 1736-1749, 1931-1964, 2099-2108, 2168-2169, 2925-2930 | Filter, timeline, badge, group |
| S5 | `src/Kronikol/Reports/report-duration-filter-function.js` | 1-20 | Filter on time left (not SHA-pinned; `VerdictFencesTests` pins four other scripts) |
| S5 | `src/Kronikol/Reports/context-menu-script.js` | 25 | Strip the marker |
| S5 | `src/Kronikol/Reports/warm-up-styles.css` (new) | | Appended only when drawn |
