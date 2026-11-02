# Calls and events plan: #220

**Written:** 2026-11-02, at 5.27.13 (`v5.27.13`, `c58d98c0`; main at `27cc0e46`). **Status: executing, green-lit.** The owner
answered on 2026-11-02: all three releases, R1 as one patch, and a closing comment that says why the fix went further (Q1, Q2, Q8 in
§10). The same day they asked for the plan to be made complete and accurate, with any deep dive that could change it, and §12 records
what that second pass changed. Then they said "I want it fixed", so the open questions that shape a release were taken at their
recommendations (§10), and the releases are being built. Each release was first prototyped in a throwaway worktree, and its patch is
kept in the harness.

Evidence labels: **RUN** (measured here, against the published 5.27.13 packages unless the line says otherwise),
**READ** (in the source, `file:line` at `27cc0e46`), **DOC** (a vendor's own documentation, fetched 2026-11-02),
**INFERRED** (reasoned from facts, stated by none), **ISSUE** (taken from #220, not re-measured). Every RUN line has
its probe and its output, or the summary line of its run, in [`CALLS_AND_EVENTS_PLAN.harness/`](CALLS_AND_EVENTS_PLAN.harness/README.md).

#220 reports that Spanner, BigQuery and Bigtable requests are logged with `MetaType: Event`, so each query's note is
drawn on the event card and the call is counted as an event. It asks for that line to be removed from the three
trackers. The report is right about Spanner. The suggested edit is right too, but it is incomplete, and it does not reach the cause.

## 0. Summary

1. **`MetaType` is a value each tracker picks by hand, with no rule and nothing checking it.** So it is wrong in both directions. Three
   database trackers log `Event`. Four messaging packages log a publish as `Default`, and four log an ack or an admin call as `Event`.
   Only Service Bus of the nine broker packages follows one rule on both sides. Its documented test, "fire-and-forget", cannot be
   applied: a Pub/Sub publish has an answer and the tracker logs it.
2. **A merge used that value for an unrelated question.** `MergeableReportReader.ReadMethod` treats `Event` as "free text". At `Default`
   it reads any casing of the nine HTTP verbs as a verb, and the writer upper-cases a verb. So a label such as `Delete` comes out of
   `kronikol merge` as `DELETE`. This is live in every merge at default verbosity for Cosmos DB, EF Core, BigQuery's handler, Blob
   Storage, Storage Queues, Redis's request and any gRPC or proxy method a consumer named after a verb (F2). It reaches the merged
   data file, `kronikol query`, `Failures.md`, the Background calls table and `service.json`, not a merged diagram, which is the shard's text. The issue says a merged report "reads the labels as before":
   measured, it does not, and the suggested edit alone would add a hand-called BigQuery tracker's `Delete` and Spanner's Raw `Delete`.
3. **With `--history`, that rewrite becomes a false alarm (F1).** Seven runs making identical calls: the seventh run's own report says
   `nothing changed`, and the same run through `kronikol merge --history` says `behaviour changed`, with the reason "same status,
   different calls". The verdict reaches the merge's console line, its History tab, the labs page, the CI summary and, on a scenario that keeps failing,
   `Failures.md`. **Its reach looks narrow, but is not fully known:** the wiki documents the option (`Cross-Run-History.md:760`), yet no template, action or CI
   here passes it, neither BreakfastProvider's CI nor this repository's runs a merge at all, and `kronikol history gate` and `query history` over a merged report
   have behaviour verdicts off, so it cannot fail a build (RUN, READ). How many consumers pass the option is unknown. The first draft of this plan called it the most
   serious finding. What was measured does not support that: F2 reaches more people, and the issue's own defect is the most visible.
4. **The issue's scope is narrower than its title in one way and wider in another.** Only Spanner has an automatic route that logs
   `Event`. BigQuery's automatic route, its message handler, already logs `Default`, and Bigtable has no automatic route at all. On the
   other side, the same kind of error runs through the messaging trackers (F5, F6).
5. **A merge differs from the run's own reading in other ways, none about kind (F20, F21).** R1 removes the method from those differences. In the
   real data that leaves no difference in any call line over 20,781 scenario positions, and 4 in a result, all in ingest reports, which a merge refuses. It does not claim to remove the rest,
   and with the ledger visible at run time a real lane still reads one false verdict (F20): §3.1 says where each goes.

**What this plan does:** define the field (§5.1), stop a merge from reading it and keep every method as the run wrote it (§5.2), correct
the three trackers and guard the rule (§5.3), all in one patch that closes #220 (R1). Then make OTLP's span kinds follow OTel's table
(R3) and apply the rule to the messaging trackers (R2). **The prototypes change the order the owner approved:** R2 without R3 moves
three receives from a right `CLIENT` to a wrong `PRODUCER`. R3 without R2 makes no tracker's row worse (RUN, 202 rows), at two narrow costs that §5.5 names, so this plan recommends shipping R3 before R2 (Q12).

Prototype results: on R1 the real data's 20,781 scenario positions make the same call lines with and without a merge, and all ten of the original
history scenarios read `stable`. R2 is 8 source files with one test pin to flip. R3 is larger than the first draft said (§5.5).

## 1. How far each claim was checked

| Claim in #220 | Verdict | Level |
|---|---|---|
| `SpannerTracker.LogRequest` logs `Event`, so the note is on the event card | Holds. `SpannerTracker.cs:66`, `PlantUmlCreator.cs:416,487` | READ, RUN |
| So do `BigQueryTracker` (line 55) and `BigtableTracker` (line 57) | Holds of those classes, but not of the calls most consumers make: F3, F4 | READ, RUN |
| Every other database tracker logs `Default` | Holds. No other package with a statement-shaped category writes `Event` | READ |
| `kronikol query interactions --group-by service,kind` calls it an event | Holds. BreakfastProvider: `Spanner Event`, 12 calls | RUN |
| Warm-up detection leaves Spanner out | Holds, but narrowly. Read as `Default`, BreakfastProvider's report gets 18 marks, the same 18: its Spanner calls are made by the service under test, which `WarmUpCalls.cs:155` already excludes | READ, RUN |
| OTLP gives each Spanner call the span kind `Producer` | Holds. `OtlpSpanMapper.cs:254`. OTel says a database call SHOULD be `CLIENT` | READ, DOC |
| The component diagram and the service card are unaffected | Holds. `ComponentDiagramGenerator.Protocol` returns the category first (`:542`), and the card goes through it (`ServiceCard.cs:145`, `CallGraphBuilder.cs:64`) | READ |
| History doesn't read `MetaType` | Holds of `src/Kronikol/History/`, which never names it. **False in effect:** a merge reads the method through `MetaType`, and a merge's history is built from what it read (F1) | READ, RUN |
| `ReadMethod` reads a free-text label at `Default`, "so a merged report reads the labels as before" | **False** for any label that is a verb in another casing: `Delete` becomes `DELETE` (F1, F2) | RUN |

## 2. Where it stands today

### 2.1 What the field says it means

`RequestResponseMetaType` (`RequestResponseLog.cs:248`): `Default` is "a standard request/response exchange (HTTP call,
database query, etc.)", `Event` is "a fire-and-forget event (e.g. message publish, event send)". The data file's schema
says the same (`ReportGenerator.cs:8113`), and so do the wiki's `Generated-Reports.md:416` and
`Ingesting-External-Captures.md:33`. Its only per-operation use in a tracker, `ServiceBusTracker.cs:63-68`, was
described when it shipped as "Messaging operations (Send, Receive, Schedule, Peek) use `MetaType.Event` for blue
async-messaging rendering" (`CHANGELOG.md:18247`), which is closer to what it has come to mean.

### 2.2 Who writes it

RUN (`kinds-by-tracker`, 202 rows): every operation of every route, driven through the shipped code, behind the real SDK
client where there is a handler. The counts are operations. Bold breaks the rule of §5.1.

| Package, route | Publish, receive | Settle, manage | A database call | Its response |
|---|---|---|---|---|
| Spanner, every route | | | **`Event`** (18) | `Default` |
| BigQuery, `BigQueryTracker`, hand-called | | | **`Event`** (9) | `Default` |
| BigQuery, `BigQueryTrackingMessageHandler`, automatic | | | `Default` (28) | `Default` |
| Bigtable, `BigtableTracker`, hand-called, its only route | | | **`Event`** (7) | `Default` |
| Every other database package (READ) | | | `Default` | `Default` |
| Service Bus | `Event` (6) | `Default` (11) | | as its request |
| Pub/Sub | `Event` (4) | **`Event`** (4) | | `Default` |
| Event Hubs | `Event` (5) | **`Event`** (6) | | `Default` |
| Kafka | `Event` (7) | **`Event`** (9) | | as its request |
| MassTransit | `Event` (3) | **`Event`** (3, faults) | | as its request |
| SQS | **`Default`** (3) | `Default` (11) | | `Default` |
| SNS | **`Default`** (2) | `Default` (10) | | `Default` |
| EventBridge | **`Default`** (2) | `Default` (26) | | `Default` |
| Storage Queues | **`Default`** (3) | `Default` (8) | | `Default` |
| Core `MessageTracker` | `Event` (8) | | | as its request |

Of the nine broker packages, only Service Bus follows the rule on both sides. Each package's `Other` is left out: it is
`Event` in four and `Default` in the rest.

**Reach of the wrong rows (READ, RUN).** The 10 false negatives are all on automatic routes. The false positives mostly are not:

| Operations | How a consumer meets them |
|---|---|
| Pub/Sub 4, Event Hubs 6 | No wrapper logs them, so only a hand call does: `Classify` is called only at `TrackingPublisherClient.cs:36,62`, `TrackingSubscriberClient.cs:40`, `TrackingEventHubConsumerClient.cs:111`, `TrackingEventHubProducerClient.cs:53,79,101` |
| Kafka 9 | Off by default (`KafkaTrackingOptions.cs:40-44`), and logged as one instant (`KafkaTracker.cs:360-369`) |
| MassTransit 3 | On by default, on a failed send, publish or consume only |

So R2's visible effect under default options is the 10 publishes and receives moving to the event card, MassTransit's faults moving off
it, and each reply's note following its request.

### 2.3 Who reads it, and what each reader is really asking

| Reader | Where (READ) | The question it is really asking | Is kind the right input? |
|---|---|---|---|
| The note's card | `PlantUmlCreator.cs:416,1191`, style at `:1266` | Does this note belong to a message operation? (a receive's request note is its poll parameters, a send's reply note a message id) | Yes, once the field is right |
| `query`'s `kind` | `QueryCommand.GroupBy.cs:164`, `QueryCommand.Payloads.cs:354`, `ReportScanner.cs:880` | Message or call? | Yes, once the field is right |
| Warm-up | `WarmUpCalls.cs:155` | Its comment says "an event a broker delivered is not a request the test made". Its test skips the test's own publish too | Partly (F12, Q5) |
| OTLP span kind | `OtlpSpanMapper.cs:254` | Which of five OTel kinds? It has two answers (F10) | Partly |
| **Merge** | `MergeableReportReader.cs:509-511` | Is this method free text? | **No.** Nothing about a message tells you that |
| Protocol fallback | `ComponentDiagramGenerator.cs:533,542`, `CallGraphBuilder.cs:64`, through `ServiceCard.cs:145` | What to call a call with no category | Harmless: every broker package hard-codes its category (RUN). Of the options only `MessageTrackerOptions.DependencyCategory` can blank one, and `CaptureRedaction.Custom` can rewrite any record (§5.4) |
| Collapse key | `SequenceCollapser.cs:138` | Are two calls the same? | Harmless today. R2 splits one key at `Raw` on three AWS handlers (§5.4) |
| The data files, ingest | `ReportGenerator.cs:6030,6419,6841`, `InteractionRecord.cs:279,324` | They carry it | Yes |

The report's scripts never read the field (READ: no `metaType` in any `.js`). The note's look travels through the
`<<eventNote>>` stereotype, so a note that loses it needs no script change.

## 3. Findings the issue does not state

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **A merge reports changed behaviour where none occurred.** Identical runs: the run's own report says `nothing changed`, its merge says `6 behaviour changed` over the first probe's routes and `8 behaviour changed` over the routes F2 adds. It reaches the merge's console line, its History tab (`MergeCommand.cs:138`, `MergeableReportRenderer.cs:135`), the labs page, the CI summary and, on a scenario that keeps failing, `Failures.md`. A scenario that newly broke reads `broke`, since a change of status outranks it. Measured on the published 3.11.0 as well, the first release with `merge --history`; until 3.25.3 (`4ee3842d`) a folder input failed on `History.run.json`, so only a shard named as a file reached it. **Reach:** the wiki documents the option (`Cross-Run-History.md:760`, `Merging-Parallel-Reports.md:65,110,307`, `Marking-New-and-Changed-Tests.md:151`), but no template, action or CI here passes it (`Merging-Parallel-Reports.md:52,197`, `README.md:172`, the PR report link template and the history action do not), and how many consumers do is unknown. `history gate` and `query history` over a merged report have behaviour verdicts off, since no fragment sits beside it, so `--fail-on behaviour-change` exits 0 | RUN, READ | `history-across-merge`, `history-in-failures`, `history-routes`, `f1-at-3.11.0`, `merged-report-readers`; `MergeCommand.cs:250` |
| F2 | **A merge rewrites these labels everywhere it writes them:** its data file, `kronikol query`, `Failures.md`'s Call column, the Background calls table and `service.json`, though not a diagram, which a merge copies from the shard (`MergeableReportReader.cs:112-114`), where a call's `op` id moves and a Storage Queues `DELETE` reads as a message of no direction, since `MessageTable.Read` is ordinal (`:121`). At default verbosity: Cosmos DB `Delete`, `Patch`; EF Core `Delete`; BigQuery's handler `Delete`; Blob Storage `Delete`; Storage Queues `Delete`; Redis `Get`, `Delete` on the request (the response is `Get (Hit)`); any gRPC unary method or `DispatchProxy` method a consumer named after a verb. At `Summarised`, also MongoDB `Delete`, Cloud Storage `Delete`, Elasticsearch `Get`, `Delete` and gRPC's streaming methods. A consumer's own label at `Default` goes the same way. Older than F1: since 3.6.0 (`f95ae4b5`). A merge needs `GenerateMergeableData`, off by default (`ReportConfigurationOptions.cs:300`) | RUN, READ | `verb-labels`, `verb-routes`, `verb-others`, `merge-labels`, `real-report-merge-diff`, `card-queue` |
| F3 | **One BigQuery call has two kinds, by route.** The handler, which is how a client is tracked, logs `Default`. `BigQueryTracker` logs `Event`, and nothing in the package calls its `LogRequest`. BreakfastProvider's BigQuery calls read `Default` | READ, RUN | `BigQueryTrackingMessageHandler.cs:69`, `BigQueryTracker.cs:55` |
| F4 | **Bigtable has no automatic route.** The package is the tracker and its options, so only a consumer calling `LogRequest` by hand meets this | READ | `src/Kronikol.Extensions.Bigtable/` |
| F5 | **Four messaging packages log a publish or a receive as a call.** 10 operations, among them SQS `SendMessage` and SNS `Publish`: a plain note, kind `Default`, while a Pub/Sub publish in the same diagram is on the event card | RUN | list (b) of the harness README |
| F6 | **Four log settlement and management as events.** 22 operations, among them Pub/Sub `Ack`, Kafka `Commit` and Event Hubs `GetPartitionIds`. Most need a hand call or a flag (§2.2) | RUN, READ | list (c) of the harness README |
| F7 | **The documented test is false.** A Pub/Sub publish has an answer, and the tracker logs it ("a publish's message id") | READ | `PubSubTracker.cs:93` |
| F8 | **A response's kind depends on the package.** 55 operations log an `Event` request and a `Default` response, and the diagram reads a response's own kind for its note's card, so a reply's note is on the event card from Service Bus and on the plain note from Pub/Sub | RUN, READ | list (d) of the harness README; `PlantUmlCreator.cs:1191` |
| F9 | **Documentation states what the code does not.** `TrackSendMessage` says "standard (non-event) styling" and sets `Event`, on purpose since `CHANGELOG.md:17243`. `Component-Diagrams.md:187,188,590` says the protocol follows `MetaType`, where a category wins. Several pages call the event card blue, where the page paints the theme's `--dg-event` (`#eee1f7` light, `#2a2140` dark; `report-tokens.css:81,200`, `inline-svg-styles.css:128`) | READ | `MessageTracker.cs:235-238`, §8 |
| F10 | **OTLP has two span kinds where OTel has more.** All 93 `Event` operations export `PRODUCER`, among them deliveries, which OTel calls `CONSUMER`, and acks, which it calls `CLIENT`. 34 of them carry a `db.system.name` as well, and OTel says a database call SHOULD be `CLIENT`. No `messaging.*` attribute is written, though the spec requires `messaging.system` and `messaging.operation.name` | RUN, DOC, READ | `OtlpSpanMapper.cs:254` |
| F11 | **The way back loses it too.** `SpanToInteractionMapper` reads the span's kind and `messaging.system`, and writes no `metaType`, so an ingested `PRODUCER` span is `Default`. Messaging capture is off by default (`OtlpTapOptions.cs:14-15,21`), so only a tap user who turned it on meets this | READ, RUN | `SpanToInteractionMapper.cs:54`, `r3-way-back`, `r3-real-capture` |
| F12 | **Warm-up's reason is narrower than its test.** It says "an event a broker delivered", and skips the test's own publish as well | READ | `WarmUpCalls.cs:155` |
| F13 | **The Java port disagrees with .NET both ways.** Its Bigtable recorder copied `EVENT`, and its hand-called `AwsTracking` logs SNS and SQS sends as `EVENT`, where its `AwsExecutionInterceptor` logs `DEFAULT`, as .NET's handlers do. Its writer upper-cases the whole method slot (`GET (HIT)`), .NET's behaviour before 3.6.0, and no ledger row records that. Its merge reads its own fragment format, so `ReadMethod` has no Java twin, and a .NET merge refuses a Java report | READ, RUN | Kronikol4J `77c2351`: `BigtableInteractionRecorder.java:63`, `AwsTracking.java:93,106`, `AwsExecutionInterceptor.java:95-96`, `ReportDataSerializer.java:868`, `FragmentJson.java:10` |
| F14 | **The suite pins the bug and misses the reader.** Three facts assert `Event` on the database trackers, and the merge theory has no verb-cased label. With only those three flipped, the prototype passes the whole core suite. Its fixture quotes `Orders/Place [unary]`, a gRPC label no tracker writes: Raw is `/orders.Orders/Get [UnaryCall]`, Detailed and Summarised the method name | RUN | `SpannerTrackerTests.cs:94`, `BigQueryTrackerTests.cs:119`, `BigtableTrackerTests.cs:94`, `MergeReadsEveryTrackersMethodTests.cs:39` |
| F15 | **`MessageTracker` splits a message's trace, for OTLP.** With no HTTP context each call gets a new trace id (`:387`, and `:378` for a malformed header), so a request and its response differ, and OTLP, which pairs on both ids (`OtlpSpanMapper.cs:143`), exports two orphan spans with one (trace, span) id. `TrackSendMessage` is not affected. Not a defect outside OTLP: nothing else reads the difference | READ, RUN | `r3-trace-id` |
| F16 | **An ingest flattens these labels too, whatever the kind.** `Delete` at `Event` reads `DELETE`. It is consistent within an ingest's own stream, so it raises no false alarm | RUN | `ingest-labels`; `InteractionRecord.cs:660` |
| F17 | **OTLP's database names are not OTel's.** It writes `spanner`, `cosmosdb`, `dynamodb`, `mssql`, where the registry has `gcp.spanner`, `azure.cosmosdb`, `aws.dynamodb`, `microsoft.sql_server`. No measured backend keys on the value for a Kronikol span | READ, DOC, RUN | `OtlpSpanMapper.cs:355-369`, `r3-backends` |
| F18 | **Where a shard's run id does not come from CI metadata, a merge counts its own recorded run as an earlier one.** A merge takes no run id, and derives one from the shard's `ciMetadata`, never its environment (`MergeCommand.cs:250-252`, `HistoryRunBuilder.cs:229-240`). On simulated Actions and Azure DevOps the ids match, and a merge reads the same before and after `history record`. Off CI, on a provider Kronikol does not detect with `HistoryRunId` set as the wiki advises, and on Actions with a custom id, a recorded run reads `still failing` and one earlier run too many. `Merging-Parallel-Reports.md:309` advises recording first, which contradicts `Cross-Run-History.md:716-721` | RUN (simulated environments), READ | `ci-run-ids`, `history-in-failures` |
| F19 | **`kronikol export` of NDJSON gives a database call an HTTP verb.** F16's flattening means a Cosmos `Delete`, a Spanner `DELETE` or a Redis `GET` gets `http.request.method` and a verb for a span name, beside its `db.system.name`, where the live route does not. Span kinds are unaffected | RUN | `otlp-verb`, `r3-export-verbs`; `OtlpSpanMapper.cs:212` |
| F20 | **A shard that can see the ledger declares itself partial, and the fold keeps it.** `HistoryRunContext.cs:183-185` sets `Partial` from the shard's own reading, and `HistoryLedgerWriter.cs:665-667` makes the run partial if any shard says so, where `HistoryCommand.cs:386-391` and `Cross-Run-History.md:384` say the heuristic runs at append "so the shards of one run do not each declare themselves partial". The history action's flow does this, as it runs `read` in each test job before the tests. Real lane: `212 scenarios from 2 shards partial`. Nothing is then reported absent, and a merged run, which is not partial, reads past those lines: with `--history` it compares with an older run and raises a false `behaviour-changed` that R1 does not remove (C1), or stays silent on a real change (C2). Not about kind | RUN, READ | `merge-round-trip-*` |
| F21 | **A merge differs from the run's own reading in other ways, none about kind.** Rarer and hand-built cases: a path ending in `%20`; `ResultDefaulted`; rows sharing a stable id, result, attempt, ticks and error; an `ExampleValues` null in a `.krkl`; two features with one display name. Options a merge never reads: `HistoryBranch`, `HistoryCountRuns`, `HistoryMinRuns`, `HistoryReordered`, `HistoryPartialRun`. Silence across a rule change; a false new dependency from rule 6 to 11; a tool older than its package (`3 behaviour changed` against own 1); merge order deciding a scenario in two shards; a retry in a second process, as `runs/` is not swept. Over 132 real reports and 20,781 positions, R1 has 0 differences in call lines, where 5.27.13 has 138, all casing. 4 positions differ in result, all in ingest reports, which a merge refuses | RUN | `merge-round-trip-*` |
| F22 | **SQS, SNS and EventBridge log no response when a send throws** (`SqsTrackingMessageHandler.cs:113`, `SnsTrackingMessageHandler.cs:113`, `EventBridgeTrackingMessageHandler.cs:119`). Storage Queues has answered them since #125 (`StorageQueueTrackingMessageHandler.cs:119-126`). Under R2 these become orphan `Event` records. Answering them changes a call's status in the fingerprint (`Caller>SQS SendMessage /orders -` becomes `... !HttpRequestException`), so it needs a history rule of its own, as rules 9 and 11 had: `RecordedBefore.NotRecorded` on the response, an `Unanswered` mark, and a `HistoryAcrossTheFixTests` per package | RUN, READ | `failed-sends-*`, `r2-citations` |
| F23 | **The wiki lists routes that do not exist.** `Integration-EventHubs-Extension.md:79-89` lists operations no wrapper logs, and its line 99 contradicts line 81. `Integration-PubSub-Extension.md:75-79` does the same | READ | R2 prototype |
| F24 | **`kronikol merge <folder>` exits 1 on a real report folder.** It sweeps `attachments/*.json` as shards, so a shard with `attachments/openapi.json` stops the merge. The wiki's recipe can hit it. Not about kind | RUN | `merge-folder-attachments` |
| F25 | **A record with a relative URI costs part of the report,** with a warning: "... not supported for a relative URI". It always loses the scenario's diagram. With internal flow on, its default, it also loses `TestRunReport.html` and `Specifications.html`. With `GenerateMergeableData` on as well, it also loses `TestRunReport.krkl` and `service.json`. With flow off, only the diagram is lost. Not about kind | RUN | `relative-address-report`, `merge-round-trip-*` |
| F26 | **A browser test is flaky, with or without these changes.** `BackForwardRestoreTests.R16_Back_lands_on_the_same_scenario_after_the_features_above_it_were_drawn`, run alone, fails 2 of 10 on clean `27cc0e46` and 2 of 10 with all three patches. The one failure in the full suite was its WebKit row. Not about kind | RUN | `e2e-r16-control` |
| F27 | **`Kronikol.Tests.ProxyTap` fails now and then, and the cause is not shown.** 3 of 31 whole-project runs on all three patches have one failure, a different fact each time; 0 of 30 on clean `27cc0e46`, which is too few to tell apart. None came back once the messages were being kept. Each of the three facts takes a port from `StubUpstream.FreePort`, which lets it go and listens on it later. `LoopbackPort` guards that within a process and not against another process, and other sessions' suites shared the machine. No patch touches the package, so a port race is the likely cause (INFERRED) | RUN | `test-runs-all-three` |

### 3.1 Every finding and where it goes

| Finding | Fixed in | Held by |
|---|---|---|
| The issue's defect, F3, F7 | R1 | §6.3, the doc comment for F7 |
| F4 | Left: Bigtable keeps no automatic route, and §6.3's fact covers its tracker | §11 |
| F1, F2, F14 | R1 | §6.1, §6.2, and F14's flips in §6.3 |
| F9 | R1 | §8, a `grep` in §6.6 |
| F5, F6, F8 | R2 | §6.7 |
| F12 | R2, through Q5 | §6.7 |
| F23 | R2 | §8 |
| F10, F11, F15 | R3 | §6.8 |
| F19 | R3 removes `http.request.method`. The span name stays flattened through NDJSON (F16, Q9) | §6.8 |
| F17 | R3, if Q10 says so | §6.8 |
| F13 | Kronikol4J's own issue (Q7), and its ledger row in each release | The ledger |
| F16 | Left (Q9). Its harms are F19, half fixed by R3's gate, and F2's on an ingest. Q9 (a) accepts them | |
| F18 | Own issue (Q11). R1 corrects the wiki's false sentence | §8 |
| F20, F21's path ending in `%20` and rows that look alike | R4, not R1 | R1's facts name what they do not cover (§6.2) |
| The rest of F21 | Own issue (Q13) | |
| F24, F25 | R4 | |
| F22, F26 | Own issues (Q14) | |
| F27 | Q14, and §6.6 looks again before each tag | §6.6 |

## 4. The underlying problem

`MetaType` carries one bit that has no rule. Each tracker's author picked a value by hand: the three GCP trackers
arrived together in `2717358e` (2026-04-27), with facts that pin `Event`, in the shape (INFERRED) of the Pub/Sub
tracker added six days earlier (`26967725`), whose changelog entry reads "Logs request/response pairs with Event MetaType"
(`CHANGELOG.md:18149`). The facts therefore assert the bug. Nothing compares a tracker's kind with its `DependencyCategory`, although
`DependencyCategories.IsStatementShaped` knows what a database is, or with `CallGraph/MessageTable`, which already knows,
per broker and per label, whether a call publishes, receives or neither, and has its rows held to each broker's
classifier by that package's tests. So the codebase holds two truthful classifications and a hand-set bit that
disagrees with both.

The documented test makes it worse. "Fire-and-forget" cannot be applied: a Pub/Sub publish returns a message id, and
`PubSubTracker` logs it ("a publish's message id", `PubSubTracker.cs:93`). #220 reasons the same way, "it
has an answer, and the tracker logs one", which would make a Pub/Sub publish a call too.

And because the bit was the only thing on a record that looked like "not HTTP", other code leaned on it for a question
it does not answer. `ReadMethod`'s own remarks record that the premise "only an event carries a free-text label" is
wrong (`MergeableReportReader.cs:494`). That fix (`f95ae4b5`, 3.6.0) stopped the crash, kept `Event` as the test, and matched
the verbs in any case. It is the same commit that stopped the writers upper-casing a whole method slot, so a run's file and its
merge first disagreed there. `c9c3b068` (3.11.0) then built a merge's history from the records it read, which made the
disagreement a false verdict.

So there are three defects under the one symptom, in the order they reach people:

1. **The bit has no rule and no guard** (F3 to F9). Every Spanner consumer sees this on every diagram. It is why the issue exists, and
   why the next tracker will repeat it.
2. **A merge rewrites labels** (F2), which reaches every merge, and with `--history` raises a false verdict (F1).
3. **Two readers ask the bit for more than it knows** (F10, F12).

## 5. The design

### 5.1 The rule

`Event` means **the call moves a message**: its operation hands one to a broker, or reads one from it, or delivers one to a consumer.
`Default` means everything else: a database query, an HTTP or gRPC call, settling a message, managing a topic or a queue. It says
nothing about whether an answer comes back, and it is fixed before the answer: an empty receive, a failed send and a peek are events too, where the call itself is recorded.
MassTransit records a failed send, publish or consume only as a fault record (`TrackingSendObserver.cs:34-42`, `TrackingPublishObserver.cs:34-42`, `TrackingConsumeObserver.cs:33-42`), which holds the
exception and no message, so it is `Default` (Q4), and a failed MassTransit send leaves no `Event`.

This is `MessageLabelKind` said in one sentence: `Publishes`, `Receives` and, from R3, `Delivers` are `Event`, and `Neither`
("settlement, management", `MessageTable.cs:3`) is `Default`. The rule goes in the enum's documentation, the schema's description and
the wiki, and the facts in §6 hold the trackers to it.

The field stays what it is: a value the writer sets, public, and in the data file. A consumer who logs their own
`Event`, or calls `TrackSendEvent`, keeps exactly what they have (`Event-Annotations.md`). No reader overrides a
written value, with one exception that changes no record: OTLP's exporter gives a statement-shaped category written as `Event` the span kind `CLIENT` and no `messaging.*` (§5.5), since OTel says a database call SHOULD be `CLIENT`. The record keeps its `Event`. That reaches a consumer's own `Event` on a database call, and a capture from before R1.

The enum's new documentation:

```csharp
/// <summary>Whether a record is a call or a message operation.</summary>
public enum RequestResponseMetaType
{
    /// <summary>A call: a database query, an HTTP or gRPC call, settling a message, managing a topic or a queue.</summary>
    Default,

    /// <summary>
    /// A message operation: one that hands a message to a broker, reads one from it or delivers one to a consumer, whatever
    /// came back. An empty receive and a failed send are events too. A record written only to say a call failed, holding an
    /// exception and no message, is a call. Drawn on the event card.
    /// </summary>
    Event
}
```

### 5.2 A merge reads a method by how it is written, never by its kind (R1)

The writer draws the line: `ReportGenerator.MethodText` (`:6003`) writes a real verb and keeps a label as captured. So the reader only
has to ask for the capitals a standard verb is written in:

```csharp
private static OneOf<HttpMethod, string> ReadMethod(string method) => method switch
{
    "GET" => HttpMethod.Get, "POST" => HttpMethod.Post, "PUT" => HttpMethod.Put, "DELETE" => HttpMethod.Delete,
    "PATCH" => HttpMethod.Patch, "HEAD" => HttpMethod.Head, "OPTIONS" => HttpMethod.Options, "TRACE" => HttpMethod.Trace,
    "CONNECT" => HttpMethod.Connect,
    _ => method
};
```

**And the writer keeps a verb as it was built:** `HttpMethod verb => verb.Method` in `MethodText`, in place of `verb.ToString().ToUpperInvariant()`.
That is a no-op for every standard verb, and it closes the one case the reader cannot: `new HttpMethod("Patch")`, `("patch")` or
`("Purge")` through the real HTTP handler (`TestTrackingMessageHandler.cs:267`). History keeps those as built (`InteractionShape.cs:451`), so
today's writer gives `PATCH` against a recorded `Patch`, and a false alarm on 5.27.13 and on the reader change alone (RUN). The real
data holds none of these (RUN), and the change costs one line. The first cut of R1 lacked it, and a merge check found it (§12).

An all-capitals label (`DELETE` from SQL or Spanner's `Summarised`) reads as a verb and writes back as the same text, so
the round trip is exact either way, and the fingerprint with it. Only four places branch on verb against label
(`InteractionShape.cs:451`, `WarmUpCalls.cs:250`, `ReportGenerator.cs:6005`, `OtlpSpanMapper.cs:212`): the first three give the same text,
and OTLP is never fed a merged read (`ExportCommand.cs:175,201`) (READ, RUN). No writer has ever put a mixed-case `HttpMethod` in a file:
the writers before `MethodText` upper-cased the whole slot, and `MethodText` has upper-cased a verb since 3.6.0 (READ, at `v2.27.11`,
`v3.5.1`, `v3.6.0`, `v3.11.0`, `v4.0.0`, `v5.0.0`). No fixture under `tests/`, `examples/` or `schemas/` holds one.

**No history rule moves.** RUN: `kronikol merge --history` does not write to the ledger (8,234 bytes before and after), and
a ledger's lines come from the run's own fragment, which holds the label as captured. So the fix makes a merge's method agree
with what is already recorded, and no bridge is needed. What the fix does not make agree is F20 and F21.

**The rejected alternative is reading the method case-insensitively in the fingerprint.** It would need a rule move and a bridge, it
would hide a real change of casing, and it leaves F2 standing.

### 5.3 The three trackers, and a guard (R1)

Remove `MetaType: RequestResponseMetaType.Event` at `SpannerTracker.cs:66`, `BigQueryTracker.cs:55` and
`BigtableTracker.cs:57`, as the issue asks. That covers every Spanner route, since the ADO.NET command, the gRPC interceptor
and the server observers all go through `LogRequest` (READ). After it, BigQuery's two routes agree.

The guard is what stops the next tracker: a fact in the core suite that no package whose category is statement-shaped
writes `Event` (§6). The core suite's source guards already work this way.

The note moves from the event card to the plain note. On the page that means the theme's `--dg-event` fill to the plain note's, and
in the PlantUML source from the `.eventNote` style (11 px, rounded, `#cfecf7`, `PlantUmlCreator.cs:1265-1277`) to 13 px. That
is the fix the issue asks for and the visible change. `noteFontSize` reads the stereotype when it measures a note
(`collapsible-notes-script.js:2038`), so wrapping follows without a script change (READ).

### 5.4 The messaging trackers (R2)

The rule, applied where §2.2 is bold. Each tracker knows its operation at every verbosity, including `Raw`, where SQS, SNS,
Storage Queues and EventBridge record the HTTP request itself and MassTransit its transport's URI (`rabbitmq://`,
`MassTransitOperationClassifier.cs:73`), so `MessageTable` cannot read the label. So the kind is set from the operation, as
`ServiceBusTracker.cs:63-68` already does, and no shared code is needed. **Prototyped:** 8 source files, +68 -10, 21 log sites.

- **To `Event`:** the publishes and receives of SQS, SNS, EventBridge and Storage Queues (10 operations).
- **To `Default`:** the settlement and management of Pub/Sub, Event Hubs and Kafka (19), MassTransit's faults (3, Q4), so a failed MassTransit send leaves no event, and
  `Other` wherever it is `Event` (4): it is a call the classifier could not name, so nothing says it moved a message.
- **Unchanged:** Service Bus, and `MessageTracker`, whose every method moves a message.
- **A response carries its request's kind** (F8, Q3), which Service Bus, Kafka, MassTransit and `MessageTracker` already do. Responses
  whose kind differs from the request go from 21 to 0 at every verbosity (RUN).

Borderline, left as the table says (`Neither`): EventBridge `StartReplay`, Storage Queues `UpdateMessage`, Kafka `Flush`.

What a failed send leaves (RUN, `failed-sends-*`):

| Package | Records | Their kinds, 5.27.13 to R2 |
|---|---|---|
| SQS, SNS, EventBridge | The request only (F22) | `Default` to `Event` |
| Storage Queues | Request and response | `Default` to `Event`, both |
| Pub/Sub, Event Hubs | Request and response | `Event` and `Default` to `Event`, both |
| Service Bus, Kafka | Request and response | `Event`, both, unchanged |
| MassTransit | A `Send Fault` pair only, or nothing with `LogFaults = false` (READ, `MassTransitTrackingOptions.cs:30`) | `Event` to `Default`, both |

**Warm-up must change with it (Q5).** `WarmUpCalls.cs:155` skips every `Event`. R2 would therefore lose real marks: a test's own SNS, SQS,
EventBridge or Storage Queues send with a slow first call, and an uncalled worker's publish, go from `first` to none. Measured through the
real trackers, 31 shapes (RUN, `warm-up-shapes`):

| Slow first call | 5.27.13 | R2 alone | R2 and Q5 (b) |
|---|---|---|---|
| Test's SNS, SQS, EventBridge or Storage Queues send | first | **none** | first |
| Uncalled worker's SNS publish | first | **none** | first |
| SQS long poll (the first call is waiting for a message) | first, falsely | none | none |
| Storage Queues peek | first | none | none |
| Test's Kafka produce, Service Bus send, Pub/Sub publish or Event Hubs send | none | none | **first** |
| Pub/Sub `Ack` or Event Hubs `GetPartitionIds` by hand, worker never called | none | first | first |
| Test's send or receive at `Raw` on SQS, SNS, EventBridge or Storage Queues | first | **none** | **none** |
| Kafka hosted commit, Pub/Sub hosted subscriber, MassTransit faults | none | none | none |
| Test's MassTransit send or publish, MassTransit on `rabbitmq://`, `MessageTracker` on `event://` or a relative address | none | none | none |
| SQS delete, hosted Service Bus `Complete` | first | first | first |

The change is one condition: an `Event` is skipped unless `MessageTable` reads it as `Publishes`. The probe's view and the option's code agree on 31 of 31 shapes, and 115 of 115
warm-up facts pass, with 20 new rows (`prototype/r2-option-warm-up-judges-publishes.patch`). It reads the scheme through `MessageTable.Scheme`, as a record's address may be relative: the first cut read
`Uri.Scheme` and threw, so no report was written where 5.27.13 warns and writes most of one (RUN, `relative-address-report`, and a row of the new theory holds it). MassTransit's publishes are judged and hold no time, as both
records are logged together after the send (RUN). `plans/WARM_UP_PLAN.harness/warmup.py:147` does not need the table: no workflow runs it, the parity fact reads stored marks, and none of
the corpus's 638 test-made events reads as a publish, so a 23-line port changes none of its 400 marks (RUN, `warmup-spec-cost`). The class doc's exception (`WarmUpCalls.cs:46`) grows a clause, and
`A_publish_the_test_makes_is_judged` holds it. Residual: at `Raw`, SQS, SNS, EventBridge and Storage Queues lose their marks; MassTransit on `rabbitmq://` is outside the table and has no time to judge;
`MessageTracker` on its own scheme stays unjudged, as today. BreakfastProvider's report cannot tell these apart: of its 1,290 requests in scenarios R2 moves none, and its marks are 18 in every view, the option's code among them (RUN).

`MessageTable` is the oracle only at one remove: each broker's card class has a direction map, already held to the table by
`Each_call_..._reads_as_its_operation`, and eight of §6.7's nine theories use that map, as the table cannot read `Raw` or `Consume`. MassTransit's reads the operation, as its map calls a fault a message at `Raw`
(`MassTransitCardMessageTests.cs:30-35`).

Readers that move: the note's card, `kind`, and warm-up. Also **the collapse key, at `Raw` on SQS, SNS and EventBridge only, with
`CollapseConsecutiveIdenticalCalls` on (off by default):** `POST x4` becomes `POST x2, POST x2`, as one key now holds both kinds (RUN,
`readers-of-kind`). A category wins in the protocol fallback, and no broker package offers an option to blank it. `CaptureRedaction.Custom`
can rewrite a record, and with a category blanked that way an ack edge reads `HTTP` and an SQS send its label (RUN). That is a changelog line,
not worth code.

**R2 cannot be accepted on BreakfastProvider**, as none of its lanes holds a call R2 moves. Its proof is §6.7 and the matrix. No real report here uses
those brokers, so the size of the visible change is unmeasured.

Kronikol4J's `AwsExecutionInterceptor` logs these as `DEFAULT`, as .NET's handlers do today, so R2 opens that difference on the automatic route, and its ledger row says to mirror `EVENT`. Only the hand-called `AwsTracking` logs `EVENT` already.

### 5.5 OTLP's span kinds (R3)

OTel's table (DOC, semantic conventions v1.44.0, latest release 2026-08-04, the messaging page's status `Development`; "Span kind SHOULD be set
according to the following table"; a database span "SHOULD be `CLIENT`", an HTTP span "MUST be `CLIENT`", both pages `Stable`):

| Operation type | Span kind |
|---|---|
| `create` | `PRODUCER` |
| `send` | `PRODUCER` if the context of the "Send" span is used as creation context, otherwise `CLIENT` |
| `receive` (pull) | `CLIENT` |
| `process` (push, "not initiated by the application code") | `CONSUMER` |
| `settle` | `CLIENT` |

What each is in Kronikol's terms needs a third verdict in `MessageTable`, since today's `Receives` lumps pulls with pushes and Kafka's and MassTransit's
consumes read `Unknown`: `MessageLabelKind { Publishes, Receives, Delivers, Neither, Unknown }`.

| Broker | Publish | Pull (`CLIENT`) | Delivery (`CONSUMER`) |
|---|---|---|---|
| Kafka | `Produce`, `ProduceAsync`: **injects `traceparent`** (`KafkaProduceTracking.cs:120-121`) | | `Consume`, whichever route. The handler route is a span, the wrapper's default a poll recorded as an instant (`TrackingKafkaConsumer.cs:91-94`), which by the letter is `CLIENT`, but the label cannot separate them |
| Service Bus | `Send`, `SendBatch`, `Schedule` | `Receive`, `ReceiveBatch`, `Peek` (`CreateProcessor` is untracked) | |
| Event Hubs | `Send`, `SendBatch` | `ReadEvents`, `ReadEventsFromPartition`, `Read` | `ProcessEvent`, `Process`, by hand |
| Pub/Sub | `Publish`, `PublishBatch` | `Pull`, by hand | `Receive`: its subscriber calling the handler (`TrackingSubscriberClient.cs:32-55`) |
| SQS | `SendMessage`, `SendMessageBatch` | `ReceiveMessage` | |
| SNS, EventBridge | `Publish`, `PublishBatch`; `PutEvents`, `PutPartnerEvents` | | |
| Storage Queues | `SendMessage`, `Send` | `ReceiveMessages`, `PeekMessages`, `Receive`, `Peek` | |
| MassTransit | `Send`, `Publish`, `→` | | `Consume`, `←`: an observer around a pushed consumer |
| `MessageTracker`, a consumer's own | Free label: never read in process, so a send | | `TrackConsumeEvent`: `NoteOnRight`, on any scheme and under any label |

So the mapper reads, in order, and the first that answers wins:

1. Not `Event`: `CLIENT`.
2. A statement-shaped category written as `Event`: `CLIENT`, and no `messaging.*`. The record keeps its `Event` (§5.1).
3. An HTTP verb on `http(s)`: `CLIENT`. HTTP's table says MUST, and at `Raw` the label cannot separate SQS's send from its receive.
4. `NoteOnRight`, which only `TrackConsumeEvent` sets: `CONSUMER`, on any scheme and under any label.
5. `LabelIsFree`, the rest of `MessageTracker`: nothing is read, as the label is its author's words, so it is a send (step 8).
6. The row of the record's scheme in `MessageTable`, MassTransit's `Raw` label included: `Delivers` is `CONSUMER`, `Receives` and `Neither` are `CLIENT`, `Publishes` is a send (step 8).
7. Only a record read back from NDJSON, or one a span or a merge made, as neither mark is on the wire: the label's whole first word. `Consume`, `Process` and `Deliver` are `CONSUMER`; `Receive`, `ReceiveMessage`, `ReceiveMessages`, `Pull`, `Poll`, `Peek` and `PeekMessages` are `CLIENT`.
8. A send, and anything else at `Event`: **Q6.** (a) `PRODUCER`, which is how the patch reads, or (b) `CLIENT` except Kafka's produce, which is OTel's letter, as only Kafka's tracker injects a context (`prototype/r3-option-send-is-client.patch`). By the letter 86 of the 89 `PRODUCER` rows are `CLIENT`, and the other three are `TrackProduce` (RUN). Two Kafka routes inject nothing and still read `PRODUCER`: `PropagateTraceContext` off, and `LogProduce` by hand (RUN, `r3-kafka-creation-context`).

**What R3 needs beyond the kind.** None of this was in the first draft:

- **`messaging.*` attributes.** `messaging.system` and `messaging.operation.name` are Required, `messaging.operation.type` and
  `messaging.destination.name` Conditionally Required (DOC). They change no measured edge, but the spec requires them and the tap drops a `PRODUCER` that has no
  `messaging.system` (RUN). The tap keeps reading `messaging.system` alone: a span that names its system only as `messaging.system.name` stays uncaptured, so R3's tap gains no call (§7).
- **The consumer swap follows how the record was logged.** A `CONSUMER`'s resource must be the consumer, or Tempo draws app to broker (RUN). `TrackConsumeEvent` on every scheme, Kafka and MassTransit log the broker's arrow into the consumer, so
  the mapper swaps those back. The rows of Pub/Sub and Event Hubs log from the consumer (`DeliveryIsFromTheConsumer`), and a record a span or a merge saw keeps its parties (`SpanSource`, `MergedSource`).
- **Two internal marks on the log, never written out:** `LabelIsFree`, set by `MessageTracker`, and `ReadFromRecord`, set by `InteractionRecord.ToLog`. `NoteOnRight` alone could not separate a `TrackSendEvent("Consume", ...)` from a Kafka tracker's `Consume`. Neither mark survives NDJSON, so step 7 reads the label there: 167 of 194 rows keep their kind, no tracker's own row differs, and the 27 that differ are hand-logged (19 under Q6 (b)). `TrackSendEvent("Process order", "Orders", event://orders)` reads `Consumer Orders` through NDJSON, where it reads `Producer App` in process and on 5.27.13 (RUN, `r3-worse-kinds`).
- **The way back (F11).** `SpanToInteractionMapper` reads `messaging.operation.type` first and the kind as a fallback, because a real Kafka poll is `CLIENT` with type `receive`, so "a `PRODUCER` or `CONSUMER` span" would leave it at `Default`. A settle reads `Default`. `InteractionMerger.Adopt` loses the span's kind today, which the first cut fixes. The real capture: Confluent.Kafka's contrib 0.3.0-alpha.4 against a real broker and MassTransit 8.5.12, through protobuf into a live tap: 8 spans, 6 mapped, and all 6 return as `Event`. Under Q6 (a) all 6 keep their kind and `service.name`; under Q6 (b) 3 of them, MassTransit's `Send` and both Kafka `Send`, come back `CLIENT`, as a record keeps no span kind (RUN, `r3-real-capture`, `r3-option-send-is-client-real-capture`). Exempting a span's own record cures those and sends Kronikol's own `Publish` back as `PRODUCER`, so it is not kept. MassTransit's 2 `CONSUMER` spans have no `messaging.system`, so the tap ignores them.
- **F15.** `MessageTracker` keeps one trace id at `:387` and `:378`: 2 spans and 2 orphans become 1 (RUN). Two cases remain: a request inside an HTTP context with its response outside, and NDJSON with `traceId` on one half. Pairing on `RequestResponseId` alone closes both (OTLP 399/399, the core's filter 30/30, `r3-test-runs`, `r3-trace-id-keyed-on-pair-id`) and matches the wiki's contract, at the cost (INFERRED) of a capturer that reuses ids across traces. It is measured and left out of the patch.
- **F19.** `http.request.method` only on `http(s)` (`OtlpSpanMapper.cs:212`), in the patch: all 8 non-web rows that carried it on 5.27.13 lose it (RUN). The span name stays flattened through NDJSON: `Delete` in process, `DELETE` through NDJSON (F16, Q9).

What backends draw (RUN, `r3-backends`, read by edge):

| Span | Collector 0.164.0 | Tempo 2.10.10 | Jaeger 2.22.0 |
|---|---|---|---|
| Flat `CLIENT`, peer | app to peer | app to peer | none |
| Flat `PRODUCER`, with or without `messaging.*` | **none** | app to peer | none |
| Flat `CONSUMER`, swapped | none | broker to app | none |
| Flat `CONSUMER`, unswapped | none | app to broker, backwards | none |
| Parent and child | `messaging_system` edge | `messaging_system` edge | producer to consumer |

Kronikol's spans are flat, with no parent and no links (READ). So on the Collector today a Pub/Sub publish draws no edge and an SQS send does. Q6 (a) with R2 would take SQS's edge away (INFERRED from this table, not run through the real tracker). Under Q6 (b) no Collector edge is lost, and the sends of Pub/Sub, Service Bus, Event Hubs, MassTransit and `MessageTracker` gain one. On Tempo, at either Q6 value, six rows' app-to-broker edge (Pub/Sub `Receive`, Event Hubs `ProcessEvent`) becomes broker-to-app, which corrects a backward edge. A Kafka produce and its consume in one POST draw one of two Tempo edges today and both on R3 (c14, c15). This table comes from hand-written payloads posted with curl: it says what each shape of span draws, and `r3-span-kinds` says which shape each record takes.

`Kronikol.csproj:56` grants the OTLP package core's internals, so `MessageTable` needs no new public surface (RUN). `kronikol export` uses the same mapper, with step 7 for what the wire loses. `CallGraphBuilder` takes one line for `Delivers`, and the card does not move (RUN, 796 card facts). **Prototyped:** 16 files, 9 source +259 -31 and 7 test +712 -3 (`prototype/r3-first-cut.patch`). The literal table of this section's first draft got 18 of 147 rows wrong with the whole suite green, because nothing pins a message's kind, and the five steps of its second draft got 44 of the final probe's 194 wrong (RUN, `r3-span-kinds`).

**Order.** R2 without R3 moves three receives (`ReceiveMessage`, `ReceiveMessages`, `PeekMessages`) from a right `CLIENT` to a wrong `PRODUCER` (RUN). R3 without R2 makes no tracker's row worse: of 202 rows 0 are worse, 69 better and 11 still wrong (4 `Other`, and 7 sends under Q6 (a)), because step 1 returns `CLIENT` for any record not at `Event` (RUN, `r3-worse-kinds`). So R3 ships first (Q12): the order cures the receives, and Q6 (b) cures the Collector edges. New captures of R1's database rows need nothing, as they are `Default`. An NDJSON file from before R1 takes step 2.

**R3 has two narrow costs, at any order.** A hand-logged record through NDJSON, above. And **a tapped publish's warm-up mark:** F11 moves a Messaging-on tap's record from `Default` to `Event`, which `WarmUpCalls.cs:155` skips, so an uncalled worker's slow first publish through the tap goes from `first` to none. That is 10 of 10 shapes on R3 alone and 8 of 10 with Q5 (b), as `Send orders` on `kafka://` reads `Unknown`. A Kafka poll's false `first` goes with them. Settle, HTTP and database calls keep theirs. Messaging capture is off by default, so only a tap user who turned it on meets this (RUN, `r3-warm-up-tap*`).

### 5.6 Rejected

- **Only the issue's three lines.** Extends F2 to its own trackers (RUN), leaves the live one, leaves nothing to stop a repeat.
- **Keep `Event` on these trackers and change the note's card by category.** Fixes one reader of six: `kind` and OTLP stay wrong.
- **Correct the kind at read time** (a `CallKind.Of(log)` that readers ask, from category and label). It would also mend
  old reports, but it overrides a value a consumer set on purpose (§5.1), and reports are written fresh every run, so old
  reports are not worth that.
- **Stamp it centrally in `RequestResponseLogger.Log`.** The same objection, and the logger has no operation to read.
- **Split the enum** (`Publish`, `Delivery`). It would give OTLP and warm-up their answers directly, but it is new public
  surface and a new value in the data file's schema that Kronikol4J and every reader of the file must learn. §5.5's `MessageTable` route is enough.
- **Match verbs case-sensitively only for `Default`.** Pointless: kind has no bearing on the question.
- **Read a method case-insensitively in the fingerprint.** §5.2.
- **A shared helper for the broker trackers' kinds.** Each package has its own operation enum, and the prototype needed none.

### 5.7 What does not change

The enum, its members, and every public signature. The `metaType` field and its values. A consumer's own `Event`
records. The component diagram and service card, since a category wins. History's rule (§5.2, §7). Which arrows are drawn: only a
note's card moves, as #220 observed, apart from R2's collapse at `Raw` with collapsing turned on (§5.4).

## 6. Tests

Red first, as `CLAUDE.md` asks. Each fact below is new unless it says otherwise. Before the tag, the new facts run against
5.27.13 to prove they are red, and a fact that stays green there is called out in the log rather than claimed.

### 6.1 A merge keeps what the run wrote (R1)

In `tests/Kronikol.Tests/Reports/Merge/MergeReadsEveryTrackersMethodTests.cs`:

- `A_label_that_is_a_verb_in_another_casing_keeps_its_casing`: a theory over `Get`, `Post`, `Put`, `Delete`, `Patch`,
  `Head`, `Options`, `Trace`, `Connect` and `get`, read back through `MergeableReportReader`. **Already written and run on
  the prototype** (it is in the harness's patch): all ten rows are red on 5.27.13's `ReadMethod` and green on §5.2's
  (RUN, `red-green-merge-facts`).
- `Each_verb_in_capitals_is_read_as_a_verb`: a theory over the nine, also in the patch, green on both.
- `A_verb_built_by_hand_is_written_as_it_was_built`: `Patch`, `patch`, `Purge`, `PURGE`, `GET`. In the patch.
- `The_kind_of_a_call_has_no_say_in_how_its_method_is_read`: `DELETE` and `Delete`, each at `Default` and at `Event`, read
  the same way.
- The fixture's `Orders/Place [unary]` becomes a label a tracker writes (F14).

### 6.2 A merged run's method reads as the run's (R1)

New `tests/Kronikol.Tests/History/MergedRunHistoryTests.cs`, driving `kronikol merge --history` as
`history-across-merge` does:

- `A_merged_run_reads_as_nothing_changed_against_its_own_ledger`: a theory over the labels of F2's default-verbosity list, among them Redis's
  `Get`, Storage Queues' `Delete` and a gRPC method named `Get`, and a verb built by hand as `new HttpMethod("Patch")`, which stays red on the reader alone.
  Red on 5.27.13.
- `A_real_change_of_call_is_still_named`: the last run makes a different call, and the merged history says
  `behaviour-changed`. Green today, and it is what keeps the fact above from passing vacuously.
- `A_scenario_that_keeps_failing_is_given_no_false_lead`: `Failures.md` and the CI summary of a merge, with no "different
  calls". Red on 5.27.13 (RUN, `history-in-failures`).
- `A_merged_report_History_tab_says_nothing_changed`: the tab `MergeableReportRenderer` writes, as it is F1's most visible surface.
- `A_merge_writes_nothing_to_the_ledger`: the byte count before and after. §5.2's "no rule moves" rests on this.
- `A_merge_keeps_a_calls_operation_id`: `service.json` of a merge, with the shard's `op` id and no message row for a Storage Queues `Delete`. Red on 5.27.13 (RUN, `card-queue`).

Each runs in CI's order, with the run not yet in the ledger, and with shards that could not see the ledger at run time, so F18 and F20 cannot hide or stand in for the defect. These
facts do not claim the rest of F20 or F21, and the file's summary says so.

### 6.3 The trackers log what the rule says (R1)

- `SpannerTrackerTests.cs:94`, `BigQueryTrackerTests.cs:119` and `BigtableTrackerTests.cs:94` assert `Event` today: they
  flip to `Default`, and each fact's name with it (`LogRequest_Uses_default_metatype`). These three lines are why the bug has been green since 2.24.0.
- Spanner, through each automatic route: the ADO.NET command, the gRPC interceptor and the server observers each log
  `Default`.
- `BigQuery_logs_one_kind_whichever_route_records_the_call`: the tracker and the handler agree.
- `A_database_call_is_drawn_on_the_plain_note`: each of the three trackers' logs through `PlantUmlCreator`: no
  `<<eventNote>>`, and no `.eventNote` style block, since `AddEventStyling` adds one only when something needs it.
- `No_database_package_writes_an_event`: a source guard in the core suite, in the style of `CultureInvariantSourceTests`. It walks
  `src/Kronikol.Extensions.*` and `src/Kronikol/Sql`, and fails where a package whose tracker names a statement-shaped category writes
  `RequestResponseMetaType.Event`. `Kronikol.Extensions.Otlp` is exempt by name, as it maps every category and reads the token. A second fact checks that the walk really read Spanner, BigQuery and Bigtable, so the
  guard cannot pass on an empty list.

### 6.4 What the readers then see (R1)

- In `tests/Kronikol.Tests.Spanner`, through the real `SpannerTracker`, since `WarmUpCalls` reads no category and the core suite does not reference the package: `A_database_query_the_test_makes_itself_is_judged` (slow first, red today)
  and `A_database_query_the_service_makes_is_not` (the second condition of `WarmUpCalls.cs:155` still holds).
- `A_spanner_call_is_a_client_span`, carrying its `db.system.name`, through the real tracker. Red today.
- `--group-by category,kind` reads a Spanner call as `Default`, through the real tracker. Red today. A hand-built record at `Default` would be green, and prove nothing.
- Round trip of the data file: `metaType` absent from the XML and YAML writers at `Default` (`ReportGenerator.cs:6419,6841`).

### 6.5 What is painted (R1)

`CLAUDE.md` asks for Playwright facts on a UI change, and a decision pinned only on markup has been a real miss
(`plan-execution-audit-checklist`). In `tests/Kronikol.Tests.EndToEnd`, which gains a reference to the Spanner package so
the real tracker writes the call: `A_database_note_is_on_the_plain_card_and_a_publish_on_the_event_card` reads the
painted fill of a Spanner call's note and of a `MessageTracker` publish's note in one diagram, in both themes, against the theme's
`--dg-event` read from the page (`#eee1f7` light, `#2a2140` dark, not `#cfecf7`, which the PlantUML source holds).
It uses `PollingInterval = 200` and no network mocking, per the repo's rules.

### 6.6 Proof before the tag (R1)

- Mutations, each caught: `ReadMethod` back to any case; its `Event` branch back; `MethodText` back to upper-casing; the line back in each of the three trackers;
  `No_database_package_writes_an_event` with its category read emptied.
- The probes re-run on the released packages: `merge-labels`, `history-across-merge`, `history-routes`, `card-queue`, `real-report-merge-diff`.
- BreakfastProvider's local in-memory lanes on the release: `--group-by category,kind` reads `Spanner Default`, and a script
  reading the painted DOM finds the Spanner note on the plain fill and a Pub/Sub publish on the event fill.
- `tests/Kronikol.Tests.ProxyTap` run 30 times on the branch with each failure's message kept, on each of the three releases (F27): a port race reads as such, and anything else stops the tag.
- `git grep` the wiki and `src/` for "blue", `fire-and-forget`, `MetaType` and `eventNote` (F9).
- The whole of `CLAUDE.md`'s release check: the full suite, then every package moved together.

### 6.7 The rule over every broker (R2)

In each broker package's suite, beside the facts that already hold its `MessageTable` row to its classifier, a theory over
operation and verbosity: `Event` exactly when the card class's direction map says it publishes or consumes. **Written for all nine in the patch:** 384 rows,
green on R2, 135 red on R1's trackers, and Service Bus green on both (RUN). The first draft's form, "`Event` exactly when the table says `Publishes` or `Receives`", disagrees with correct
records on 3 of 132 rows at Detailed, 15 of 132 at `Raw` and 3 of 129 at Summarised (RUN, `kinds-r2-analysis`), as the table cannot read `Raw` or `Consume`. It moves 36 operations, §5.4's 32 and four `Other`.

- `KafkaTrackerTests.LogTransaction_Uses_event_metatype` flips, and is renamed `LogTransaction_Uses_default_metatype`. It is the only existing pin R2 turns red, out of 1,762 (RUN), so the theory is the real guard.
- `A_response_carries_its_requests_kind` for each package.
- The warm-up rows of §5.4, if Q5 says (b).
- A paint fact, with SQS and Pub/Sub references added to `Kronikol.Tests.EndToEnd`: an SQS send and a Pub/Sub publish in one diagram sit on the same card.

Proof: the matrix `kinds-by-tracker` on the release gives 0 bold rows of §2.2. No real broker and no BreakfastProvider lane proves R2 (§5.4).

### 6.8 OTLP (R3)

The patch adds 152 OTLP cases and 60 core cases net, so `tests/Kronikol.Tests.Otlp` goes from 247 to 399 and passes, as does the whole core on R1 and R3, 11,662 passed and 2 skipped of 11,664. On R1's behaviour OTLP fails 111 of 399 across 19 facts, and the core's filter 12 of 399 across 5. The `MessageTable` facts need the new API, so they were not run red. On the first cut's code the facts added after the review fail 45 of 399 across 9 (RUN, `r3-test-runs`). Q6 (b) flips 41 cases in 8 OTLP facts and 1 core case, and adds one fact (RUN).

- One row per line of §5.5's broker table. The OTLP test project references only OTLP and core, so a tracker's real record reaches it through nine more references or through `r3-span-kinds`, whose 194 rows are the acceptance. Its expected column is hand-written, so it is checked against OTel's table, not against the code.
- `kronikol export` over an NDJSON file: no tracker's row changes kind, and `Read_back_from_a_record_its_label_is_all_that_says_what_it_is` pins the hand-logged rows that do.
- A round trip with `IncludeSpanSourced` on: a `PRODUCER` messaging span with `messaging.system` ingests as `Event` and exports as `PRODUCER` under Q6 (a). Under (b) it exports `CLIENT`, real Kafka spans among them, as their label is `Send` and not the tracker's `Produce` (RUN). A poll of type `receive` reads `Event`, and a settle `Default`.
- `TrackSendEvent` with no HTTP context exports one span, not two.
- `A_verb_on_no_web_address_exports_no_http_method` (F19), which also pins the span name it leaves.
- Two pins flip: `Event_meta_type_maps_to_producer_kind`, and `Each_scheme_reads_its_trackers_labels("eventhubs","Process ← telemetry","Receives")`, now `Delivers`.
- `Db_categories_reverse_map_to_db_system_name` flips if Q10 says (b).

Proof: the probes `r3-span-kinds` and `r3-real-capture` on the release. `r3-backends` is the source of §5.5's edge table and proves nothing on the release, as it runs no Kronikol code.

## 7. Releases

Shipping order, if Q12 agrees: **R1, then R3, then R2.** The labels stay, as the patches and findings use them.

**R1, a patch: a merge keeps what the run wrote, and database calls are calls.** §5.1, §5.2, §5.3, §6.1 to §6.6. Closes #220.
A bug fix, with no new option and no new public member. Its changelog entry says what readers
will see differently:

- A Spanner query's note is on the plain note, its kind is `Default`, and its span is `CLIENT`, as for a hand-called BigQuery or
  Bigtable tracker.
- A merged report keeps labels such as `Delete` as the run wrote them, where it wrote `DELETE`, and its history no longer
  reports those calls as changed behaviour. A merged `service.json` keeps each call's operation id.
- A verb built by hand, such as `new HttpMethod("patch")`, is written as built, where the data file upper-cased it.
- A Spanner query the test itself makes is now judged by the warm-up rule.

Both parts go in one release (Q2): the tracker change must not reach anyone before the reader change. It is small: two methods
rewritten, three lines removed, a rule stated, and the facts of §6. The prototype's source diff is 15 lines added and 29 removed.

**R3, a minor: OTLP follows OTel.** §5.5, §6.8. Larger than R1 and R2 together, and it changes what a trace backend draws, so its changelog says so in terms,
gives §5.5's edge table and names its two narrow costs.

**R2, a patch: messaging follows the rule.** §5.4, §6.7. Report output moves for SQS, SNS, EventBridge, Storage
Queues, Pub/Sub, Event Hubs, Kafka and MassTransit, and warm-up's marks with it (Q5), so the changelog lists what moves from which card.

**The bumps.** R1 and R2 are patches: a bug fix that necessarily changes observable behaviour, with no new option or public member, and the changelog says what moved (`CLAUDE.md`). **R3 is a minor** (Q15): it adds four exported attributes
that a dashboard can filter on, and 5.15.0 moved the minor for one new OTLP attribute, `kronikol.double` (`CHANGELOG.md:1994-1997`). It also changes a documented output, `Exporting-to-OpenTelemetry.md:29`, which it rewrites in the same release. That is no new option and no changed default, so it is not a major under `CLAUDE.md`, but it is the release to look at twice. Every package moves together, per the repo rule. Each release's number is chosen at its push, since others may land first.

**Cross-run history.** No rule moves in any of them. A line is `Caller>Service METHOD target status`
(`InteractionShape.cs:16-20`), which holds no kind, so R1's trackers, R2 and R3's kinds cannot change a fingerprint. For R3 that is measured: 986 lines over 23 hand-made shapes and 8 real spans, by log, by record and merged, differ only in `kind` (RUN, `r3-fingerprint-diff`). One chain does gain a line: an R3 exporter feeding a Messaging-on tap, on a tap of either version, as its span now carries `messaging.system`, so what the tap dropped becomes the call `app>PubSub Publish orders /orders OK`. It is a call that was made, on a chain no recipe describes (INFERRED), so it reads as changed once and moves no rule. R1's merge change
only makes a merge's method equal the run's recorded one. A merge bridges no rule move (`HistoryBuildOptions.Bridges`), so across a rule change it reads silence, as it does today. It does bridge consumer patterns (`MergeCommand.cs:253`).

**Kronikol4J.** Each release gets its row in `docs/REMAINING_PARITY.md`: R1's says the reader is not mirrored, names Bigtable, and mirrors the schema's text. Kronikol4J byte-compares its own
`ReportDataSchema.java` with its own copy of .NET's schema (`ReportDataSchemaTest.java:19`), so nothing goes red in either repository; the row says to mirror it and re-capture. Kronikol4J has no OTLP code and no parity obligation
there (its `README.md:271-273`), so R3's span kinds have no item, but F11 moves a Messaging-on tap user's report output, which R3's row says. F13 is Q7. The comment closing #220 is Appendix A.

## 8. Edit sites

**R1.**

- `src/Kronikol/Reports/Merge/MergeableReportReader.cs:488-532`: §5.2, its remarks rewritten (they quote a gRPC label no tracker writes, `:497-498`), and the `metaType` argument
  gone from `:552`.
- `src/Kronikol/Reports/ReportGenerator.cs:6003-6005`: `MethodText` and its doc.
- `src/Kronikol.Extensions.Spanner/SpannerTracker.cs:66`, `src/Kronikol.Extensions.BigQuery/BigQueryTracker.cs:55`,
  `src/Kronikol.Extensions.Bigtable/BigtableTracker.cs:57`.
- `src/Kronikol/Tracking/RequestResponseLog.cs:245-255`: the rule. It ships in the package, so it is part of the patch.
- `src/Kronikol/Reports/ReportGenerator.cs:8113`: the schema's description; `:8080` ("GET for a cache", where Redis writes `Get`).
- `src/Kronikol/Ingestion/InteractionRecord.cs:104`: the public NDJSON contract.
- `src/Kronikol/Reports/WarmUpCalls.cs:127-130`: the comment says what the test does.
- `src/Kronikol/Tracking/MessageTracker.cs:219-221` (`TrackSendEvent`) and `:235-238` (`TrackSendMessage`): the comments (F9), and "blue" at `:237`.
- `src/Kronikol/Query/QueryCommand.Shared.cs:12,92`: internal comments.
- `src/Kronikol/CallGraph/CallGraphBuilder.cs:216-219`: a comment R1 makes stale, and `src/Kronikol/CallGraph/CallRecord.cs:59`, which says `Event` is what "Spanner, BigQuery and Bigtable write".
- Tests: §6.1 to §6.5. `tests/Kronikol.Tests.EndToEnd`'s project gains the Spanner reference.
- Wiki (`origin/master` `670b1fc`; the local checkout is behind):
  - False after R1: `Integration-Spanner-Extension.md:36`, `Integration-Bigtable-Extension.md:23`.
  - "Blue", which the page has not painted since the theme tokens: `Event-Annotations.md:3,487`, `API-Reference.md:97`, `Excluding-Requests.md:39`, `Integration-ServiceBus-Extension.md:13`, `Tracking-Dependencies.md:1144`.
  - The rule: `Event-Annotations.md:1,3,11,122,124,258-264,497,507`, `API-Reference.md:97`, `Excluding-Requests.md:38-39`, `Generated-Reports.md:416`, `Ingesting-External-Captures.md:33`, `Tracking-Custom-Dependencies.md:18,26,313,331,448`, `Service-Bus-Tracking-Patterns.md:13`.
  - The protocol (F9): `Component-Diagrams.md:187,188,590`.
  - A merge: `Merging-Parallel-Reports.md:307-311` (the false "record first", F18), and a line in `Cross-Run-History.md` on a merge's verdicts.
  - No edit needed: `README.md`, `nuget-readme.md`, the run-end `CLAUDE.md`, `Querying-Reports.md:951`, both `commands.md:349`, `VerbTable.cs`: they say only "`kind` (metaType)".
- `CHANGELOG.md`, every package's version, the Kronikol4J ledger, the comment on #220.

**R3.** `src/Kronikol.Extensions.Otlp/OtlpSpanMapper.cs:212,218,254,257`, `src/Kronikol/CallGraph/MessageTable.cs`, `src/Kronikol/CallGraph/CallGraphBuilder.cs`, `SpanToInteractionMapper.cs` (its messaging path), `MappedSpan.cs`, `InteractionMerger.cs`, `MessageTracker.cs:378,387` and its response path, `RequestResponseLog.cs` (two internal marks), `InteractionRecord.cs` (`ToLog`), wiki `Exporting-to-OpenTelemetry.md:26,29,31,33,34,60`, its four new attribute rows and a note on Tempo's slack, `Integration-Otlp-Extension.md:115` and its alias table, `OtlpSpanMapper.cs:355-369` if Q10 says (b), §6.8's facts and two pins.

**R2.** `TrackSendMessage`'s comment is R1's, not this list's. The request and response log sites of `SqsTrackingMessageHandler`, `SnsTrackingMessageHandler`,
`EventBridgeTrackingMessageHandler`, `StorageQueueTrackingMessageHandler`, `PubSubTracker`, `EventHubsTracker`, `KafkaTracker`
and `MassTransitTracker`; `src/Kronikol/Reports/WarmUpCalls.cs:46,155` and a comment at `plans/WARM_UP_PLAN.harness/warmup.py:147` (Q5); wiki `Event-Annotations.md`'s list of what is an event, `Integration-PubSub-Extension.md:66,74-79`, `Integration-EventHubs-Extension.md:73,79-89,99`, `Integration-Kafka-Extension.md:9`, `Integration-MassTransit-Extension.md:11,173`; §6.7's facts; the Kafka pin; SQS and Pub/Sub references in `Kronikol.Tests.EndToEnd`.

## 9. Risks

| Risk | Size | Answer |
|---|---|---|
| A consumer pinning diagrams sees Spanner's notes change, and one pinning a merge's data sees `DELETE` go back to `Delete` | Certain, for those few, and each is what the release fixes | The changelog says so in terms |
| The note grows from 11 px to 13 px, so long SQL takes more room | Small | It is the size every other database's note already has, and `noteFontSize` measures from the stereotype (READ) |
| New warm-up marks on Spanner | Only where the test itself queries. BreakfastProvider: 18 marks with and without (RUN) | A correct mark, and the changelog says so |
| Shards from before and after R1 in one merge | Harmless | Rows differ in `kind` only. The diagrams draw each as recorded, and no reader compares kinds across shards (READ) |
| A library at R1 with a tool pinned older | The old tool still flattens, now for Spanner too | Named in the changelog. A tool behind its package misreads a window already (F21) |
| A tool at R1 with a library pinned older | A verb built by hand keeps its false alarm until the library moves, since the old writer upper-cases it (RUN). The labels are fixed by the tool alone | Named in the changelog. The real data holds no such verb |
| An old report still says `Event` for Spanner | Certain, until its next run | Left as written (§5.6). Reports are written each run |
| A reader of `kind` was using `Default` to mean "not a broker" | Low, unmeasured: no real report here uses R2's brokers | `--group-by category` is the reliable split, and R2's changelog says what moved |
| R2 changes warm-up's marks | Certain where those shapes occur (§5.4). Q5 (b) keeps a test's publishes except at `Raw`, and adds Kafka's, Service Bus's, Pub/Sub's and Event Hubs'. MassTransit's are judged and hold no time | The table in §5.4 goes in the changelog |
| A hand-logged lower-case `get` now stays a label through a merge | Intended: it is what the run wrote | §6.1 pins it |
| A merge's real verdicts are lost with the false ones | No | `A_real_change_of_call_is_still_named` holds them |
| R3 changes what a trace backend draws | Certain for a backend's graph, at Q6 either way | §5.5's table. Under Q6 (b) no Collector edge is lost. On Tempo six rows reverse at either Q6 value, which corrects them |
| R3 costs a tapped publish its warm-up mark | Only a tap user who turned Messaging on, and an uncalled worker's slow first publish: 10 of 10 shapes on R3 alone, 8 of 10 with Q5 (b) (RUN) | Named in R3's changelog and §11 |
| A hand-logged `Event` reads another kind through NDJSON (R3) | 27 of 194 rows, 19 under Q6 (b), none a tracker's own (RUN) | Named in R3's changelog. The label is all the wire holds |
| Under Q6 (b) a real `PRODUCER` span through a tap and out again comes back `CLIENT` | 3 of 6 real rows, with `IncludeSpanSourced` on (RUN) | Named with Q6 |
| R3 follows a `Development` page | The names may move: main already has `messaging.system.name` and values such as `aws.sqs`, `gcp.pubsub` | Pinned to v1.44.0. A later move is its own patch |
| A Kafka produce and its consume share (trace, span) id, as do calls under one Activity (RUN) | Jaeger keeps both and warns `duplicate span IDs`; Tempo keeps both, after `/flush` too, though its `combine.go` dedupes on (kind, span id) (READ, Tempo's source). Compaction not measured | Existing behaviour, more visible once kinds differ. Named in §11 |
| Tempo's generator discards spans that ended over 30 s ago, so a later `kronikol export` draws no Tempo graph at any kind (READ, Tempo's source) | Existing | Named in the wiki with R3 |

## 10. Questions

**Answered by the owner, 2026-11-02:**

- **Q1. How far should this go?** All three, each released on its own.
- **Q2. R1 as one patch?** One.
- **Q8. The comment closing #220:** says that the fix went further than asked and why, naming the false alarm and that "reads as before" did not hold. Appendix A is the text, adjusted for F1's measured reach. No separate issue for F1, as R1 fixes it.

**Taken at their recommendations, 2026-11-02, on the owner's "I want it fixed":** Q3 (a), Q4 (a), Q5 (b), Q6 (b), Q9 (a), Q10 (b), Q12 (a) and Q15 (a). The owner did not answer each one, and can overturn any of them; the text of each stays below, with what it costs.

**Still open, as each only files an issue:** Q7, Q11, Q13 and Q14. "Filed only at your word" stands: nothing has been filed.

**R4, added on the same word.** `CLAUDE.md` says to fix the bugs found along the way, so F20, F21's path ending in `%20` and rows that look alike, F24 and F25 ship as a fourth release, a patch, after R2. Q13 and Q14 now ask only about what stays: the rest of F21, F22, F26 and F27. A candidate F21 item, `ResultDefaulted`, is left out, as only an ingest report reaches it and a merge refuses those.


- **Q12. The order, new since Q1.** You agreed to "R1, R2, R3 in order" on my statement that R3 needed R2. Measured, it is the other way round (§5.5). (a) R1, R3, R2. (b) R1, R2, R3, with three receives wrong between them. **Recommended: (a).** The order cures the receives, and Q6 (b) cures the Collector edges.
- **Q3. A response's kind** (R2): (a) it carries its request's, (b) keep each package's response as it logs it today. **Recommended: (a).** One rule, and a
  reply's note stops depending on the broker. It moves Pub/Sub and Event Hubs replies at `Raw`, failure messages at every verbosity (`TrackingPublisherClient.cs:50,80`, `TrackingEventHubProducerClient.cs:66,92,112`) and the ten's reply notes onto the event card. (b) is not "no change", as it makes 10 new `Event` requests with `Default` replies.
- **Q4. MassTransit's fault records** (R2): (a) `Default`, as `MessageTable` already reads them, (b) stay `Event`.
  **Recommended: (a).** They record that a send, publish or consume failed, and the table's comment says "a fault is no message". On a working send the event card holds the message; under (b) the only card on a failed send would hold the exception's text in that slot, and "`Neither` is `Default`" would be false on three operations. (a) means a failed MassTransit send leaves no event, where Kafka's and Service Bus's keep one, as those log the call (§5.4). The best argument for (b) is that a consume fault did deliver its message (`CallGraphBuilder.cs:107`), yet `kind` shows no event; the record still holds no message. They give no warm-up mark either way (RUN).
- **Q5. Warm-up in R2,** reversed since the first draft, which said a direction signal does not exist. `MessageTable.Read` gives it. (a) Leave warm-up's test: R2 loses a test's own publish marks on four packages. (b) One condition: an `Event` is skipped unless the table reads `Publishes`, which keeps those marks except at `Raw`, adds Kafka's, Service Bus's, Pub/Sub's and Event Hubs', and adds a clause to the class's exception, as `warmup.py` needs no port (§5.4). **Recommended: (b), in R2.** Without it R2 regresses a behaviour consumers have.
- **Q6. A publish's span kind** (R3): (a) `PRODUCER` for all, as the wiki says today, (b) `CLIENT` except Kafka's produce, which is OTel's letter. **Recommended: (b).** Only Kafka's tracker injects a context. No Collector edge is lost, and the sends of Pub/Sub, Service Bus, Event Hubs, MassTransit and `MessageTracker` gain one, where (a) with R2 takes SQS's away. What it costs, measured (`r3-option-send-is-client`): 86 of 194 rows move from `PRODUCER` to `CLIENT`, so a dashboard that filters on `PRODUCER` needs `messaging.operation.type=send`; a real `PRODUCER` span through a tap and out again comes back `CLIENT` on 3 of 6 real rows; and two Kafka routes that inject nothing still read `PRODUCER`. The patch holds (a). (b) is a small change to one source file and 42 test cases (`prototype/r3-option-send-is-client.patch`).
- **Q7. Kronikol4J:** file an issue for F13 when R1 ships, as `lemonlion/Kronikol4J#1` was for the service card? **Recommended: yes.**
- **Q9. An ingest's labels (F16):** (a) leave them, (b) keep a mixed-case label as written. **Recommended: (a).** External capturers
  do send `get`, which should read as a verb, and it raises no false alarm. R3 removes half of F19, the `http.request.method`. The span name stays `DELETE` through NDJSON, and F2's harms on an ingest stay (its labels, `query`, `service.json`, a Storage Queues `DELETE` as a message row): (a) accepts them.
- **Q10. OTel's database names (F17),** reversed since the first draft: (a) an issue of its own, (b) into R3. **Recommended: (b).** No measured backend keys on the value for a Kronikol span: `peer.service` outranks it on the Collector, and Tempo and Jaeger do not read it. Only a user's own dashboard could care (INFERRED). The spec says MUST, it is one table in the same file, and R3's changelog already warns backends. Cost: a pin flips, and the way back gives `gcp-spanner://`. Not in the first cut.
- **Q11. A merge counting its own run (F18):** (a) an issue of its own, (b) into R1. **Recommended: (a).** Actions and Azure DevOps are clear (simulated), and it is not about kind. R1 corrects the wiki's false sentence regardless. Filed only at your word.
- **Q13. F20 and F21:** (a) two issues of their own, (b) into R1. **Recommended: (a).** F20 reaches beyond merges, since a sharded run's line reads `partial` under the history action's flow and nothing is then reported absent, so it deserves its own look. A candidate fix exists (`Fragment()` writes only the host's `HistoryPartialRun`) and closes C1 and C2 in a copy, not run against the suite. F21 needs `merge --history` and rare inputs. Filed only at your word.
- **Q15. R3's version:** (a) a minor, (b) hold it for the next major, as it changes a documented span kind that a dashboard can filter on. **Recommended: (a).** The change follows OTel's table, it adds no option and moves no default, and the changelog gives the edge table.
- **Q14. F22, F24, F25, F26 and F27:** an issue each? **Recommended: yes.** F24 is the one most likely to bite someone: the wiki's recipe can stop on it. Filed only at your word.

## 11. What it leaves

- Reports already written keep what they say (§5.6).
- `BigQueryTracker` stays, with no caller in its package: removing a public type would be a major. Bigtable keeps no automatic route (F4).
- An ingest's labels (Q9).
- A merge counting its own run (F18, Q11), the rest of a merge's other differences (F21, Q13), and F22, F26, F27 (Q14).
- MediatR's `Publish` stays `Default` under the rule: it is in process, with no broker (READ).
- A consumer who logs `Event` by hand on a database call keeps it. The guard reads Kronikol's own packages.
- From R3's look at OTLP, each small and none about kind: `Neither` lumps settlement with management, so `messaging.operation.type=settle` waits for a split; `error.type` is not written, and span names are not per semconv; a `ConsumeFault` keeps `service.name=MassTransit`; a real SDK's SNS URI is `sns:///`, so its topic is lost (`SnsOperationClassifier.cs:13-17` parses JSON only); a `CLIENT` with `rpc.system`, the AWS shape, takes the RPC path and is dropped unless RPC capture is on; a tap's `CONSUMER` is drawn consumer to broker; spans under one Activity share an id.
- At `Raw` the service card reads a MassTransit fault as a message, because `rabbitmq` is outside the table (`CallGraphBuilder.cs:115-118`), and `MassTransitCardMessageTests.cs:30-35` pins that. Not kind: the card does not read the field.
- From R3: a tapped publish's warm-up mark where `MessageTable` cannot read its label, and a hand-logged record through NDJSON (§5.5, §9).
- Kafka's poll reads `CONSUMER` with its handler span, since the label is one. Telling them apart would change the label, and so the fingerprint.

## 12. Log

**2026-11-02, written by kronikol-6c at 5.27.13.** The first version's corrections: F1's reach was first read as stopping at the labs page, because no scenario failed in that probe and the CI summary had not been written. `history-in-failures` measured it, in CI's order too.

**2026-11-02, the same day, second pass, after the owner's answers.** Four independent investigations, each given the code and asked to break a claim. What they changed:

- **F1 was oversold.** The wiki documents `--history` on a merge, but no template or CI here passes it, no CI measured runs a merge, and a gate cannot fail on it. §0 and §4 are reordered, and F2 now leads over F1. A first correction went too far the other way, saying "no documented recipe", and the independent review caught it.
- **A correction of mine was wrong.** The first log said `verb-labels` "showed Redis writes `Get (Hit)`", so a probe's Redis `Get` row was replaced. The probe had read the classifier at `Hit`. The tracker writes `Get` on the request (`RedisTracker.cs:59`, pinned by `tests/Kronikol.Tests.Redis/DatabaseExtensionsTests.cs:87`), so the replaced row was right, and `history-across-merge`'s control `Redis Get (Hit)` is a request no tracker writes. It still serves as a control, and `history-routes` measures the real one.
- **F2 was short** by Redis, Storage Queues, gRPC and `DispatchProxy`, all at default verbosity, and did not know a merge moves `service.json`.
- **R1's first cut missed a case:** a verb built by hand. §5.2 now holds `MethodText`.
- **"A merged run reads as the run it came from" was false as worded.** It holds for the method, and not for F20 or F21.
- **§5.5 was wrong** on 18 of 147 rows, and on its order. **§6.7's theory could not be used.** **Q5 and Q10 reversed.** F18's "INFERRED" is now measured on simulated environments, and it is narrower.
- **§7's semver reason cited the wrong clause, and its fixture claim was wrong.** R3 is a minor by the precedent of 5.15.0, not a patch. The event card's colour was the PlantUML source's, not the page's.

**The same day, an independent review of the finished plan, given the code and asked to check every claim.** 16 defects, and each prototype was corrected on them. The largest: "R3 without R2 regresses nothing" was false as worded, Q6 (b)'s edge claims were unmeasured, Q5's patch threw on a relative address and lost the whole report, and the rule contradicted Q4. R3's swap now follows how a record was logged, a free label is read only where nothing else can say, a statement at `Event` is `CLIENT` with no `messaging.*`, `messaging.system.name` is gone so a tap gains no call, a merged record keeps its parties, and F19's gate is in the patch. Q6 (b) and the warm-up table are now measured, and R3's probe grew from 147 rows to 194.

On the prototypes (`CALLS_AND_EVENTS_PLAN.harness/prototype/`), each in a throwaway worktree of `27cc0e46`:

- **R1**, `r1-first-cut.patch`: §5.2 and §5.3 with the three assertions flipped and the theories of §6.1. Its remarks and docs are not rewritten, and §6.2 to §6.5 are not written. Core suite 11,602 passed, 0 failed, 2 skipped. Spanner 204, BigQuery 606, Bigtable 52. `MergeReadsEveryTrackersMethodTests`: its ten label rows fail on 5.27.13's `ReadMethod`. `history-across-merge`: ten `stable`, where 5.27.13 says six changed. `history-routes`: with the shard written by R1's own library, 15 of 15 `stable`, where 5.27.13's merge says 8 changed. R1's tool over a shard from 5.27.13's library says 2 changed, the two verbs built by hand, as the old writer has already upper-cased them in the file. `merge-labels`: the merged file differs from the shard in 0 rows, where 5.27.13 rewrites 9. Over 132 real reports with fragments, 20,781 positions: 0 differences in call lines, where 5.27.13 has 138, all casing, and 4 in a result. A real lane, 212 scenarios in two shards with no ledger at run time: `nothing changed`, where 5.27.13 says 3 changed. The same lane with the ledger visible at run time, a real change in run 4 and none in run 5: `1 behaviour changed` on R1, where the run's own view says nothing changed and 5.27.13 says 4 (F20).
- **R2**, `r2-first-cut.patch` and `r2-option-warm-up-judges-publishes.patch`, on the final R1: 8 source files, one pin flipped and renamed, the §6.7 theory. Nine broker suites and `Kafka.BuildInterception` 2,146: 2,143 passed, 3 skipped (Kafka's broker facts). Core 11,602 passed, 2 skipped, of 11,604. With the option, 115 of 115 warm-up facts. No wiki, paint fact or schema text. Not run on a real broker. MassTransit 9 will not build a bus without a licence, so its failed sends were measured through its real observers with a proxy context, as its tests do.
- **R3**, `r3-first-cut.patch`, on the final R1: §5.5 with Q6 (a), with F19's gate, without F17. OTLP 399 of 399, whole core 11,662 passed, 2 skipped, of 11,664. With R2 and Q5 (b) on top: OTLP 399, 796 card facts, and R2's real kinds equal the assumed on all 194 rows (`r3-on-the-stack`). `r3-option-send-is-client.patch`, Q6 (b) on top: OTLP 400 of 400, core filter 942 of 942. Real broker: one Kafka, through the tap.
- **All three together, with every recommended option** (R1, R3, Q6 (b), R2, Q5 (b), in that order; 46 files, source +363 -73): 54 test projects, 23,680 passed, 3 failed, 93 skipped of 23,776 (`test-runs-all-three`). The core is 11,682 passed, 2 skipped, of 11,684, and Playwright 2,089 passed, 28 skipped, of 2,117, on the bundled browsers, in 24 m 7 s. Of the three failures, two are `Kronikol.Tests.Playwright` lacking the browser its package asks for, which pass 10 of 10 with it, and one is F27. F26's fact passed this time. A first attempt lost package files from its build folders, and was built and run again (the file says how). The Playwright project holds none of §6.5's or §6.7's paint facts yet and references no Spanner or SQS package, so this says the changes broke nothing already painted, not that the new look is right.

Not done: a real Actions or Azure DevOps runner (F18 is simulated), Linux and floor builds, the `examples/` lanes, Tempo's compaction, and any backend beyond the three. F17 is not prototyped. §6.2 to §6.5 are not written. Facts are named for what the release must write, not yet written, except those in the patches.

**2026-11-02, R1 built and released as 5.27.14.** Red first: `results/r1-merged-history-red.txt`. All 8 of §6.6's mutations caught: `results/r1-mutations.txt`. Whole suite: 54 projects, 23,109 passed, 93 skipped, and 2 failed, both in `Kronikol.Tests.Playwright`, whose adapter wants a browser this machine does not hold, as on every run here: with its browser in a scratch folder all 10 pass. `Kronikol.Tests.ProxyTap` 30 times with messages kept: 30 clean (F27). An independent review of the diff, given the code and asked to break it, found no defect in the production code and 11 elsewhere, all fixed before the tag:

- **The changelog and this plan said a merge rewrote the merged report's diagrams. It never did.** A merge copies the shard's diagram text, and no probe had read a diagram. §0, F2 and §9 are corrected. What did move is the data file, `kronikol query`, `Failures.md`'s Call column, the Background calls table and `service.json`.
- **A call's `op` id moved, so `behaviourHash` does once,** which `Service-Card.md` lists among what moves the hash with no change of behaviour. It said neither.
- **`A_merge_keeps_a_calls_operation_id` could not fail on its message half.** With no declared service a card lists no message rows, so its "no row" was empty either way. It now declares one, and that half is asserted first.
- **The new Spanner facts wrote to the repository's real ledger.** That project now sets `KRONIKOL_HISTORY=off`, as `HistoryPathResolver` says test projects do. Kafka and Otlp do the same, so R4 gives them the same file.
- **The guard's own fact did not test the guard,** its root walk counted levels and it cut a line at the slashes of `spanner://`. Each is fixed. The wiki's run id sentence dropped a custom `HistoryRunId`; ten pages kept `event-style (blue) arrows` and three lines "white background"; `WarmUpCalls`' comment said "no message operation is judged", false until R2; a browser row never checked the page was dark.

## Appendix A. The comment closing #220 (Q8), to post when R1 is published

Fixed in `<version>`. Spanner, `BigQueryTracker` and `BigtableTracker` now log `Default`, so a query's note is on the plain note, `--group-by kind` reads `Default`, warm-up judges a query the test makes itself, and OTLP exports `CLIENT`.

The fix went further than the issue asked, because one of its claims did not hold in every case. `MergeableReportReader.ReadMethod` used `Event` to decide that a method was free text, and at `Default` it read any casing of an HTTP verb as a verb. A Spanner `SELECT FROM` keeps its label through a merge at `Default`, but a hand-called `BigQueryTracker`'s `Delete` and Spanner's Raw `Delete` would have come out as `DELETE`. That was already happening at `Default` for Cosmos DB, EF Core, BigQuery's handler, Blob Storage, Storage Queues, Redis and any gRPC method named after a verb.

With `--history`, it was also a false alarm. Seven runs making identical calls read `nothing changed` in the last run's own report and `behaviour changed` in its merge, because history compared the merge's `DELETE` with the `Delete` the run had recorded. The wiki documents that option, though no template passes it. A merge now reads a method as it is written and ignores the kind, and a data file keeps a verb as it was built. A run whose shards could see the ledger can still read a false change, for a reason that has nothing to do with the method, and that follows in its own release.

Two notes on reach: BigQuery's message handler already logged `Default`, so only a hand-called `BigQueryTracker` logged `Event`, and Bigtable has no automatic route, so its tracker is always hand-called.

`Event` now has a rule: the call moves a message. The messaging trackers do not all follow it yet, and OTLP's span kinds are not yet OTel's, so those follow in their own releases. The plan is `plans/CALLS_AND_EVENTS_PLAN.md`.
