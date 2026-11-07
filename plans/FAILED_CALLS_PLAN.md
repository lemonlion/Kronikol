# Failed calls plan: #232

**Written:** 2026-11-07, at 5.31.1 (`v5.31.1`, `db26e5cb`; main at `896ddad3`, whose `src/` is 5.31.1's). **Checked again and
deepened** the same day at 5.31.2 (`55999241`), which changed only the HTTP handler among the files cited (§12). **Status: draft,
not green-lit.** Nothing in it is built or published. It lives on the local branch `plan/232-failed-sends`, in its own worktree
(`C:/Code/Kronikol-232`), and nowhere else.

Evidence labels: **RUN** (measured here, against the published 5.31.1 packages unless the line says otherwise), **READ** (in the
source, `file:line` at `55999241`), **INFERRED** (reasoned from facts, stated by none), **ISSUE** (taken from #232, #140 or
`CALLS_AND_EVENTS_PLAN.md` F22, not re-measured). Wiki lines are at `7f72c19`, and Kronikol4J's at `bd47938`. Every RUN line has its probe and its output in
[`FAILED_CALLS_PLAN.harness/`](FAILED_CALLS_PLAN.harness/README.md).

#232 reports that the SQS, SNS and EventBridge handlers log a request and then nothing when the send throws, so the diagram
draws a send with no answer and nothing on the call says it failed. It suggests copying the catch Storage Queues has had since
#125, with a history rule of their own. The report is right, and the suggested edit would fix those three. It would also be the
fifth time the same catch was copied by hand into the one or two trackers an issue named: the HTTP handler and Cosmos DB in
3.18.0, ProxyTap in 4.19.5, BigQuery and Cloud Storage in 5.27.6, Blob Storage and Storage Queues in 5.27.9 (`CHANGELOG.md`
L14630, L8094, L1135, L967). And it would leave the cause in place. Measured and read here, that cause has also left calls
unanswered in nine more packages and in the SQL tracker every SQL package shares. It has recorded some failures as successes,
filed the calls made during a failed call under the next test, and written the rest in six shapes that the readers then read in
five different ways.

## 0. Summary

1. **The report is right, and it is wider than sends.** Each of the three handlers logs the request and awaits the inner
   handler with no `try` (`SqsTrackingMessageHandler.cs:120`, `SnsTrackingMessageHandler.cs:120`,
   `EventBridgeTrackingMessageHandler.cs:126`). One `SendAsync` serves every operation, so a receive, a delete or a queue's
   creation that throws leaves a request alone just as a send does (READ; RUN for the sends).
2. **The cause: nothing in Kronikol ends a call.** A call is two records, and every tracker builds both itself, in its own code,
   with its own catch or none. `FailedSend` (`src/Kronikol/Tracking/FailedSend.cs`) names a failure, but it is two formatting
   helpers. It owns no part of a call, so a tracker that never calls it still compiles, and no test asks any tracker what it
   leaves when its call throws. Each fix so far copied the catch into the trackers its issue named, and the copies already
   differ (F18, F3, F9).
3. **The same gap leaves more calls unanswered than the issue lists** (§2.1, F1 to F9). S3, DynamoDB and Atlas Data API (the same
   handler shape), every Redis command, a Pub/Sub subscriber whose handler throws, an Event Hubs read whose reader cannot be made,
   a Spanner call that fails with anything but an `RpcException`, an EF Core command that is cancelled, BigQuery's response body
   read, and any SQL call whose test or phase reads differently by the time it ends.
4. **Some failures are recorded as successes** (F10 to F13, F22). A SQL or Spanner reader that fails partway is answered as it
   is closed, with the rows read so far and no failure (RUN, on Sqlite). A ClickHouse.Driver reader is answered as soon as it is
   returned. A Spanner stream that faults with a non-gRPC exception is answered as a success. A Kafka consume scope whose
   handler throws is answered as handled (RUN). An Elasticsearch call that never reached the server is answered with no status and no `Error`
   (RUN). And a failed call can file other calls under the wrong test: the calls a server made during an HTTP call that threw,
   when `TrackingProxy` defers them, are written with the next call that succeeds, under its test (F28, RUN).
5. **Where a failure is recorded, it takes one of six shapes, and the readers disagree about them** (§2.2, §2.3). A Pub/Sub,
   Service Bus or Event Hubs failure has no status, and neither has the same call's success, so history reads a send that
   started failing as unchanged (F14). The component diagram counts only numeric codes, so it never counts a
   `!HttpRequestException` or a SQL `Error` (F19). And no view draws `Error` at all: the diagram shows `!HttpRequestException`
   on the return arrow and never why (F15).
6. **Two trackers change the exception the application sees** (F16, F17, RUN). Redis's synchronous commands go through
   `MethodInfo.Invoke` with nothing unwrapping, so the caller gets a `TargetInvocationException` with the
   `RedisConnectionException` one level inside. The same happens to an async command that throws before it returns its task.
   The in-process `TrackingProxy` unwraps a synchronous throw with `throw ex.InnerException;`. The caller gets the same object,
   but its stack now starts at `TrackingProxy`1.Invoke` and no longer names the method that threw.
7. **The fix is one way to end a call, one way to read how a call ended, and a check.** An internal `OpenCall` in core, opened
   when the request is logged and answered exactly once: with the tracker's answer, or, from the exception, with `FailedSend`'s
   shape built from the request itself, so the pairing fields, the test, the phase and the variants cannot drift. Every tracker
   that logs its request before its call ends moves onto it, and the few that write both halves at once are listed with their
   reason. `InteractionStatus` becomes the one reading every reader uses. A contract fact in every tracker's test
   project, and a guard fact in core, fail the build when a tracker can leave a call unanswered. One history rule bridges every
   fingerprint that moves.
8. **Three releases are proposed, and eleven questions** (§7, §10). R1, a patch: `OpenCall`, the guard, and every call left
   unanswered, #232's three included, under history rule 12. R2, a patch: one shape for a failed answer and one reading of it.
   R3, a minor, only if the owner wants it: the diagram draws why a call failed. Q1 recommends shipping R1 and R2 together, so
   the fingerprints move once, under one rule.

## 1. How far each claim was checked

| Claim | Evidence |
|---|---|
| The three handlers leave a send unanswered | RUN (`results/failed-calls-5.31.1.txt` L5-31; #220's `failed-sends-5.28.2.txt`), READ |
| Every operation of the three, not only a send | READ: one `SendAsync` classifies and logs every operation |
| The other unanswered paths (F1 to F9) | READ; RUN where §2.1 says so (S3, DynamoDB, Atlas Data API, Redis, the Pub/Sub subscriber, the Event Hubs read, EF Core's cancel) |
| Failures recorded as successes (F10 to F13, F22, F24) | READ; RUN for a Sqlite reader, Kafka's scope, Elasticsearch and a `TrackingProxy` stream |
| Calls filed under the next test (F28) | RUN (`results/more-failed-calls-5.31.1.txt`), READ |
| The six shapes | RUN for every shape (the probe drives at least one tracker of each), READ |
| What each reader makes of each shape (§2.3) | READ |
| Redis and `TrackingProxy` change the exception (F16, F17) | RUN, READ |
| Kronikol4J (F20) | READ at `bd47938`, not built |
| How #140 meets the reading (§5.4) | ISSUE, READ |
| What a history bridge needs | READ, from #125's two bridged moves (rules 9 and 11) |

## 2. Where it stands today

### 2.1 What a call that throws leaves, by tracker

"Unanswered" is a request with no answer. An answer's status, its note (`Content`) and its `Error` are as recorded.

| Tracker | Entry point | A throw leaves | Evidence |
|---|---|---|---|
| SQS, SNS, EventBridge | `SendAsync`, every operation (`:120`, `:120`, `:126`) | Unanswered | RUN, READ |
| S3, DynamoDB, Atlas Data API | `SendAsync` (`S3TrackingMessageHandler.cs:100`, `DynamoDbTrackingMessageHandler.cs:115`, `AtlasDataApiTrackingMessageHandler.cs:111`) | Unanswered | RUN, READ |
| Storage Queues, Blob Storage, Cloud Storage | `SendAsync` (`StorageQueueTrackingMessageHandler.cs:103-143` and the same in the other two) | `!HttpRequestException`, no note, `Error`. The caller gets the Azure SDK's `RequestFailedException`, which wraps the exception the handler rethrew untouched | RUN (Storage Queues), READ |
| BigQuery | `SendAsync` (`BigQueryTrackingMessageHandler.cs:99-134`) | `!Type` and `Error`; but the body read at `:137` is outside the `try`, so a body that fails to read leaves it unanswered | READ |
| Cosmos DB | `SendAsync` (`CosmosTrackingMessageHandler.cs:114-143`) | `!Type` and `Error`, with no phase variants: under a Setup and an Action verbosity the request has both and the answer none; no test throws | RUN, READ |
| Kronikol's HTTP handler | `TestTrackingMessageHandler.cs:297-322` | `!Type` and `Error` | READ |
| Redis | sync, async, async void (`RedisTrackingDatabase.cs:74`, `:88`, `:115`) | Unanswered on every path. A sync throw, and an async command that throws before it has a task, reach the caller as `TargetInvocationException` | RUN, READ |
| Pub/Sub | publish (`TrackingPublisherClient.cs:48`, `:78`) | Answered with no status, the message as the note | RUN (#220), READ |
| Pub/Sub | the subscriber's handler (`TrackingSubscriberClient.cs:44-49`) | Unanswered | RUN, READ |
| Event Hubs | send (`TrackingEventHubProducerClient.cs:64`, `:90`, `:110`) | No status, the message as the note | RUN (#220), READ |
| Event Hubs | read (`TrackingEventHubConsumerClient.cs:112-129`) | No status, the message as the note; unanswered when the reader cannot be made (`:116`, before the `try`) | RUN (both), READ |
| Service Bus | send and receive (`TrackingServiceBusSender.cs:44`..`:134`, `TrackingServiceBusReceiver.cs:50`..`:218`) | No status, the message as the note (none at `Summarised`) | RUN (#220, send), READ |
| Kafka | produce (`KafkaProduceTracking.cs:147-154`), consume (`TrackingKafkaConsumer.cs:114`) | `!` and Kafka's error code, `Error` | RUN (#220, produce), READ |
| Kafka | a consume scope whose handler throws (`KafkaTracker.cs:226`) | Answered with no status and no `Error`, as a handled message is, under `TrackConsume` and under `TraceMessageProcessing` | RUN, READ |
| MassTransit | send, publish, consume faults (`MassTransitTracker.cs:201-236`) | A second call, `Send Fault {T}`, status `Fault`, the message as that call's request | RUN (#220), READ |
| `TrackingProxy` (MediatR, DispatchProxy) | `TrackingProxy.cs:141-200` | `500`, note `"{Type}: {Message}"`; a synchronous throw reaches the caller with its stack replaced; a stream answered `200` (`OK`) as soon as it is returned, with the iterator's type name as its note, and never told when its enumeration throws (`:136-137`) | RUN, READ |
| `TrackingProxy`, `Deferred` mode | `DeferredLogFlushHandler.cs` (`SendAsync`), `TrackingProxy.cs:228` | The records wait for the test side's handler to flush them after a send. A send that throws skips the flush, so they go out with the next send that succeeds, under that send's test, or never | RUN, READ |
| gRPC | the interceptor (`GrpcTrackingInterceptor.cs:410-419`) | An `RpcException` answered with its status mapped to HTTP (`Unavailable` is `503`) and the note `"{code}: {message}"`; anything else `500` and `"Unknown: {message}"`; no `Error` | RUN, READ |
| Spanner | the command (`TrackingSpannerCommand.cs:86`.., `SpannerTracker.cs:104`) | `Error`, the outer message as the note | RUN, READ |
| Spanner | the interceptor (`SpannerTrackingInterceptor.cs:45-49`, `:221`) | As the command for an `RpcException`; unanswered for a sync throw or any other exception | READ |
| Every SQL tracker | `SqlDiagnosticTracker.cs:190`, `:325-365`, the commands, the transactions, EF Core's `CommandFailed` | `Error`, the outer message as the note (none at `Summarised` with `LogResponseContent` off), no `Error` | READ |
| EF Core | a cancelled command (`SqlTrackingInterceptor.cs:220-229`: no `CommandCanceled`) | Unanswered, and its entry stays in `_pendingIds`: EF Core calls `CommandCanceledAsync`, not `CommandFailedAsync`, and the caller gets `OperationCanceledException` | RUN, READ |
| SQL readers | a reader that fails partway (`TrackingDbDataReader.cs:95-102`) | Answered at `Close` with the rows so far, as a success: on Sqlite the caller gets the second row's `SqliteException`, and the answer is `OK` with the first row | RUN, READ |
| MongoDB | `CommandFailedEvent` (`MongoDbTrackingSubscriber.cs:271`, `:284`) | `500` and the server's message in `Error`, a client-side failure alike; nothing before a command starts | READ |
| Elasticsearch | the transport's callback (`ElasticsearchTrackingCallbackHandler.cs:83-109`) | A refused connection throws nothing at the caller: the client returns a response with `IsValidResponse` false and an `OriginalException` (an `HttpRequestException` over a `SocketException`). The answer has no status, no note and no `Error`, as `OriginalException` is never read | RUN, READ |
| ProxyTap | a failed forward (`ProxyTap.cs:743`, `ForwardFailure.cs:50-59`) | `!Type`, `Error`, and the 502 body the caller got | READ |
| TcpTap | a reset mid-call (`TcpTap.cs:868`) | The decoded command dropped, reported in diagnostics | READ |
| OTLP tap | an `ERROR` span (`SpanToInteractionMapper.cs:392-394`) | `500`, the span's message as the note, no `Error` | READ |

Not tracked at all, so out of this plan's reach: a connection's `Open`, a failed `BEGIN TRANSACTION`, Service Bus processors,
MongoDB's server selection, Dapper transactions, ClickHouse.Driver's `PingAsync` (§11).

### 2.2 How a failure is written: six shapes

| Shape | Status | The reason | Written by | History's line ends |
|---|---|---|---|---|
| A, `FailedSend` | `!` and the exception's type | `Error`, the message chain | HTTP handler, ProxyTap, BigQuery, Cloud Storage, Blob Storage, Storage Queues, Cosmos DB; Kafka with its error code | `!HttpRequestException` |
| B, the message as the note | none | `Content` | Service Bus, Event Hubs, Pub/Sub's publisher | `-`, as their successes do |
| C, HTTP 500 | `500` | `Content` `"{Type}: {Message}"`, or `Error` (MongoDB) | `TrackingProxy`, gRPC for a non-gRPC exception, MongoDB for a client-side failure, the OTLP tap | `500` |
| D, the word `Error` | `Error` | `Content`, the outer message only | every SQL tracker, Spanner | `Error` |
| E, a second call | `Fault` | the second call's request | MassTransit | a `Send Fault {T}` call |
| F, the gRPC status | mapped to HTTP | `Content` `"{code}: {message}"` | gRPC | e.g. `503` |
| None | | | §2.1's unanswered rows | `-` |

### 2.3 Who reads how a call ended

| Reader | Its rule | A | B | C | D | No answer |
|---|---|---|---|---|---|---|
| `InteractionStatus.IsError` (`src/Kronikol/Reports/InteractionStatus.cs:101-112`) | a code of 400 or more; text not `OK...` and not in `NonErrorStatuses` (`:84-88`) | failed | not | failed | failed | not |
| `query failures` (`QueryCommand.Narrative.cs:1092-1093`) | `IsError`, or an `Error` | failed | not | failed | failed | not |
| `Failures.md` (`FailuresDigestGenerator.cs:348`, `:390-391`, `:805-807`) | the status as text, a reason line from `Error` | raw text and reason | blank | `InternalServerError`, no reason | `Error`, no reason | blank, not flagged |
| `query flow` (`QueryCommand.Narrative.cs:764-766`, `:691`) | says `no response`; `--errors-only` reads the status only | failed | not | failed | failed | `no response` |
| Component diagram (`ComponentFlowSegmentBuilder.cs:207-212`, `:243`, `:498`) | a numeric code of 400 or more | not counted | not | counted | not counted | not |
| OTLP export (`OtlpSpanMapper.cs:464-492`) | `!` is `ERROR` with `Error`; 400 or more `ERROR`; a word list | `ERROR` with the reason | `UNSET` | `ERROR`, no message | `ERROR`, message `Error` | an orphan span (`:192-202`) |
| The diagram (`PlantUmlCreator.cs:1203-1228`) | none: a label, and a note only for a body or headers | label, no reason | empty label, the note | label, the note | label, the note | no return |
| History (`InteractionShape.cs:19`, `:346-348`) | the status as text | `!Type` | `-` | `500` | `Error` | `-` |

Five rules for one question, and `Error` read by three of the eight. An unanswered call is named only by `query flow` and by the
diagnostics page (`ReportDiagnostics.cs:48-50`, `DiagnosticReportGenerator.cs:121-128`).

### 2.4 How history reads an answer, and how #125 bridged one

A call's fingerprint line ends with its answer's status, or `-` with none (`InteractionShape.cs:19`; the status from
`InteractionStatus.Split`, `:346-348`). So answering a call that had no answer moves its line, and so does giving a status to an
answer that had none. Rule 11 is current (`InteractionShape.cs:72`), and rules 6 to 11 are kept (`:78`), so a run in a window that
holds older runs also fingerprints its calls under the older rules, and a move costs no quiet run. #125 moved twice, rules 9 and
11, and each time marked the new answer `RecordedBefore.NotRecorded` (`RequestResponseLog.cs:58-85`, `:213-220`), which every
older rule reads as no answer (`InteractionShape.cs:369-412`). Each moved package has a replay across the upgrade
(`tests/Kronikol.Tests.StorageQueues/HistoryAcrossTheFixTests.cs`), and each rule has its pins (`ShapeGuardTests.cs:141`, `:476`;
37 `HistoryGuard.Pin(` lines).

## 3. Findings the issue does not state

| ID | Finding | Evidence | Goes to |
|---|---|---|---|
| F1 | The three handlers leave any operation that throws unanswered, not only a send | READ | R1 |
| F2 | S3, DynamoDB and Atlas Data API have the same handler shape and the same gap | RUN, READ | R1 |
| F3 | BigQuery's handler answers a throwing send but reads the body outside its `try` (`BigQueryTrackingMessageHandler.cs:137`) | READ | R1 |
| F4 | Redis answers no call that throws, sync or async (`RedisTrackingDatabase.cs:74`, `:88`, `:115`) | RUN, READ | R1 |
| F5 | A Pub/Sub subscriber's handler that throws leaves its `Receive` unanswered (`TrackingSubscriberClient.cs:44-49`); #232 said so from reading the code | RUN, READ | R1 |
| F6 | An Event Hubs read makes its reader before its `try` (`TrackingEventHubConsumerClient.cs:116`) | READ | R1 |
| F7 | Spanner's interceptor catches only `RpcException`, and calls its continuation outside any `try` (`SpannerTrackingInterceptor.cs:49`, `:79`, `:221`) | READ | R1 |
| F8 | EF Core sends a cancelled command to `CommandCanceled`, which the interceptor does not override, so the request stays unanswered and its `_pendingIds` entry is never removed (`SqlTrackingInterceptor.cs:220-229`). Its event data, `CommandEndEventData`, holds no exception | RUN, READ | R1 |
| F9 | The SQL tracker reads the phase and the test again when the call ends, and drops the answer when either changed (`SqlDiagnosticTracker.cs:310-320`, since 5.11.1's rework); Pub/Sub and Service Bus read the phase again too (`PubSubTracker.cs:120`, `ServiceBusTracker.cs:144`). The phase is an `AsyncLocal` (`TestPhaseContext.cs:11`), so within one async flow the reads agree. They differ when the answer is logged from another flow, as when other code closes a reader, or when the test fetcher or the `HttpContext` reads differently by then | READ | R1 |
| F10 | A SQL or Spanner reader that fails partway is answered at `Close` as a success (`TrackingDbDataReader.cs:95-102`, `SqlDiagnosticTracker.cs:396`) | RUN (Sqlite), READ | R2 |
| F11 | ClickHouse.Driver answers `ExecuteReaderAsync` when the reader is returned (`TrackingClickHouseClient.cs:119`) | READ | R2 |
| F12 | A Spanner stream that faults with a non-gRPC exception is answered as a success at `Dispose` (`TrackingAsyncStreamReader.cs:57`, `:74-79`) | READ | R2 |
| F13 | A Kafka consume scope closes with `failureStatus: null` whatever its handler did (`KafkaTracker.cs:226`); a `using` block cannot see the exception | RUN, READ | Q4 |
| F14 | Pub/Sub and Service Bus answers carry no status on success or failure (`PubSubTracker.cs:111-120`, `ServiceBusTracker.cs:126-144`), so history reads a failed send as it reads a good one | READ | R2 |
| F15 | No view draws `Error`: nothing under `src/Kronikol/PlantUml` reads it, and the report's scripts read only `console.error` | READ | R3, Q10 |
| F16 | A synchronous Redis command that throws, or an async one that throws before it has a task, reaches the caller as `TargetInvocationException` (`RedisTrackingDatabase.cs:74`, `:88`) | RUN, READ | R1 |
| F17 | `TrackingProxy` rethrows a synchronous throw with `throw ex.InnerException;` (`TrackingProxy.cs:147`): the same object, its stack replaced. Async methods keep theirs | RUN, READ | R1 |
| F18 | Cosmos DB's copy of the catch, written in 3.18.0 when its request already had phase variants, writes none on the failed answer, and no test throws (`CosmosTrackingMessageHandler.cs:119-143`) | RUN, READ | R1 |
| F19 | The component diagram's `% errors` counts only numeric codes (`ComponentFlowSegmentBuilder.cs:207-212`, `:243`, `:498`) | READ | R2 |
| F20 | Kronikol4J has no `error` field and no `FailedSend`; its HTTP trackers log both halves only after a response, so a thrown HTTP call leaves nothing there (`TrackingHttpClient.java:75-88`); its ledger says a bare request (`docs/REMAINING_PARITY.md:1952-1962`) | READ | Q7 |
| F21 | OTLP export sends a SQL failure with the message `Error` and a `TrackingProxy` failure with none (`OtlpSpanMapper.cs:464-492`) | READ | R2 |
| F22 | Elasticsearch answers a refused connection with no status and no `Error`, though the client hands back the failure in `OriginalException`, which is never read | RUN, READ | R2 |
| F23 | MassTransit records a failed send as a second call of the default kind, with no `Error` (`MassTransitTracker.cs:201-236`) | READ | R2 |
| F24 | `TrackingProxy` answers a stream `200` when it is returned, with the iterator's type name as its note, and never sees its enumeration fail (`TrackingProxy.cs:136-137`) | RUN, READ | R2 |
| F25 | MongoDB reads a client-side failure as `500`, as it reads a server's (`MongoDbTrackingSubscriber.cs:271`, `:284`) | READ | Q2 |
| F26 | TcpTap drops a decoded command whose connection resets before the reply (`TcpTap.cs:868`) | READ | Q5 |
| F27 | `Failures.md` does not flag an unanswered call, where `query flow` says `no response` | READ | R2 |
| F28 | `DeferredLogFlushHandler` flushes the queue only after its send returns, with no `finally`. When the send throws (TestServer hands an app's unhandled exception to the client), the calls a `Deferred` `TrackingProxy` queued during it go out with the next send that succeeds, under that send's test, or never (`DeferredLogFlushHandler.cs`, `TrackingProxy.cs:228`; `Integration-DispatchProxy-Extension.md:105` says the queue is flushed after each response) | RUN, READ | R1 |
| F29 | Nothing documents which text statuses read as a success: `Tracking-Custom-Dependencies.md` does not list `NonErrorStatuses`, so a custom tracker's `Completed` or `Published` reads as failed (#140) | ISSUE, READ | R2, Q11 |

## 4. The underlying problem

A call in Kronikol is two records, a request and its answer, paired by `RequestResponseId`. Three things are missing from how
they are made and read.

1. **Nothing owns the end of a call.** `RequestResponseLogger.Log` takes one record at a time. A tracker logs a request, calls
   the real client, then builds an answer of its own that repeats a dozen of the request's fields, and logs that. Whether a call
   that throws gets an answer depends on whether its author wrote a catch. The catch, where it exists, is copied: Storage
   Queues' is 41 lines (`StorageQueueTrackingMessageHandler.cs:103-143`), and the copies already differ. Cosmos DB's drops the
   variants (F18), BigQuery's leaves the body read outside (F3), the SQL tracker looks the test up again at the end and drops the
   answer when it differs (F9), and each broker writes the message where the status should be (F14). Even their history marks differ, as each was bridged by the rule of its own release
   (§5.2). The one piece of code that
   does this right is Kafka's `OpenEntry` (`KafkaTracker.cs:422-452`): it carries the request's test, phase, label and ids, and
   builds the answer itself. It is private to Kafka.
2. **Nothing reads the end of a call the same way twice.** `InteractionStatus.IsError` is the closest thing to a shared rule, and
   five readers have rules of their own (§2.3). So one failure reads as failed in `query failures`, as nothing in the component
   diagram and as `-` in history.
3. **Nothing checks.** No test asks every tracker what it leaves when its call throws. The packages fixed under #125 have a fact
   each; most of the rest have none (§6.2).

The suggested edit leaves all three: three more copies of the catch, a rule of their own for three packages, and the next
package written without one. The fix supplies the three missing pieces and moves every tracker onto them.

## 5. The design

### 5.1 The contract

- **C1.** Every tracked call ends with exactly one answer, written by the code that opened it. A path that cannot end its call is
  named in the guard's list with its reason: a gRPC server stream never read to its end (`GrpcTrackingInterceptor.cs:444`, by
  design) is the only one today.
- **C2.** An answer that reports a failure has a status `InteractionStatus` reads as failed, and its reason in `Error`.
- **C3.** A call that threw where its service gave no status of its own is answered `!` and the exception's type, with
  `FailedSend.Describe` in `Error` and no body: `FailedSend`'s shape, documented since 3.18.0
  (`HTTP-Tracking-Setup.md:110-120`).
- **C4.** A tracker never changes the exception. What it rethrows is the object it caught, with its stack. A client above the
  tracker may still wrap it: the Azure SDK hands its caller a `RequestFailedException` around the one Storage Queues' handler
  rethrew (RUN). That is the SDK's own behaviour, the same with no tracker.
- **C5.** The answer belongs to the request's test and phase, read once, when the request was logged.

### 5.2 One way to end a call: `OpenCall` (R1)

A small internal type in core, lifted from Kafka's `OpenEntry`. Every package R1 moves can already use it, as each is in
`Kronikol.csproj`'s `InternalsVisibleTo` list (`src/Kronikol/Kronikol.csproj`, checked one by one). Elasticsearch, Bigtable and
MassTransit are not, and none needs to be: each writes both halves at once, and `FailedSend` and `Error` are public. So no
release adds a public member (Q3 asks whether `OpenCall` should be public).

```csharp
namespace Kronikol.Tracking;

/// <summary>
/// A call whose request is logged and whose answer is not. Logging the request through <see cref="Open"/> returns one, and the
/// call then ends with exactly one answer: the tracker's own, or the exception the call threw, answered as FailedSend names it.
/// </summary>
internal sealed class OpenCall
{
    internal static OpenCall Open(RequestResponseLog request);  // logs the request

    internal bool IsAnswered { get; }

    // Logs the tracker's answer, unless the call is answered already.
    internal void Answer(RequestResponseLog response);

    // Logs the answer FailedSend describes, built from the request; the caller rethrows with `throw;`.
    internal void Threw(Exception exception, Func<RequestResponseLog, RequestResponseLog>? adjust = null);
}
```

`Threw` builds its answer from the request, so nothing a copy could forget is left to the tracker.

- **Copied from the request:** the test's name and id, the method, the URI, the service and caller, both ids, `TrackingIgnore`,
  `MetaType`, `DependencyCategory`, `CallerDependencyCategory`, `Phase`, `AttributionSource`, the span ids,
  `NamedByRemoteHostFallback` and `TestDouble`.
- **Set:** `Type = Response`; `Content = null`; no headers; `StatusCode = FailedSend.Status(exception)`;
  `Error = FailedSend.Describe(exception)`.
- **Left to the logger, as for every record:** the time and the step, which `RequestResponseLogger.Log` stamps when a record
  arrives without them (`RequestResponseLogger.cs:84-91`). So a call that threw gets a duration, which an unanswered one never
  had.
- **The variants:** each of the request's phase variants, with no body and no headers (`PhaseVariant` is `Method, Uri, Content,
  Headers, Skip`: `RequestResponseLog.cs`, the record after `RecordedBefore`).
- **Not copied:** `FocusFields`, a request's own `RecordedBefore*` marks, `IsUserAction`.
- **No history mark of its own.** A mark says which rule first saw an answer, which only the tracker knows. BigQuery's and Cloud
  Storage's failed answers carry `RecordedBefore9 = NotRecorded`, and Blob Storage's and Storage Queues' `RecordedBefore11`
  (`BigQueryTrackingMessageHandler.cs:126`, `CloudStorageTrackingMessageHandler.cs:126`, `BlobTrackingMessageHandler.cs:126`,
  `StorageQueueTrackingMessageHandler.cs:134`); the HTTP handler's and Cosmos DB's, from 3.18.0, carry none. Each passes its
  own through `adjust`, and the answers R1 adds pass `RecordedBefore12` (§5.6). Were `Threw` to mark every answer, rule 11 would
  read the HTTP handler's failures, which it has always seen, as unanswered.
- **`adjust`** is for a tracker that knows more: the HTTP handler sets the response's own focus fields and its transport's span
  (`TestTrackingMessageHandler.cs:317`, `:319`), and the bridged trackers set their marks.

A second answer is dropped (an `Interlocked` flag), so a catch and a later success can never both write. The type has no
`Dispose`: a `using` block cannot see the exception (F13 is the proof), and an answer written there would be a guess.

A handler then reads, for SQS:

```csharp
var call = OpenCall.Open(new RequestResponseLog(/* as today */).WithVariants(/* as today */));
ReconstructContent(request, requestBody);

HttpResponseMessage response;
string? responseBody;
try
{
    response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    responseBody = /* read as today */;
}
catch (Exception ex)
{
    call.Threw(ex);
    throw;
}
call.Answer(new RequestResponseLog(/* as today */));
```

The trackers' own internal request methods (`ServiceBusTracker`, `EventHubsTracker`, `PubSubTracker`, `RedisTracker`,
`SpannerTracker`, `SqlDiagnosticTracker`) return the `OpenCall`, and their internal answer methods that take a failure as text
go away, so a failure can be answered only through `Threw`. Their public methods stay as they are
(`PubSubTracker.LogResponse`, `PubSubTracker.cs:86`; `EventHubsTracker.LogResponse`, `EventHubsTracker.cs:79`;
`BigQueryTracker` and `BigtableTracker`, hand-called by consumers): changing a public signature is a breaking change.

### 5.3 Every tracker on it

| Tracker | R1 | R2 |
|---|---|---|
| SQS, SNS, EventBridge, S3, DynamoDB, Atlas Data API | `OpenCall`; the send and the body read in one `try`; a throw answered (F1, F2) | |
| Storage Queues, Blob Storage, Cloud Storage, BigQuery, Cosmos DB | onto `OpenCall`, byte for byte (pinned); BigQuery's body read inside (F3); Cosmos DB's variants (F18) | |
| HTTP handler | onto `OpenCall` with its transport span through `adjust`, byte for byte | |
| Redis | `OpenCall` on all three paths; `MethodInfo.Invoke` with `BindingFlags.DoNotWrapExceptions`, so the caller gets the exception the client threw (F4, F16) | |
| Pub/Sub | the subscriber's handler answered when it throws (F5) | the publisher's failure answered `!Type` and `Error`, its note kept (F14) |
| Event Hubs | the reader made inside the `try` (F6) | send and read failures `!Type` and `Error`, notes kept (F14) |
| Service Bus | | send and receive failures `!Type` and `Error`, notes kept (F14) |
| Spanner | the interceptor's continuation inside a `try` that answers any exception (F7) | `Error` on every failed answer; a faulted stream answered as failed (F12) |
| EF Core | `CommandCanceled` and `CommandCanceledAsync` answered through `Threw` with `new OperationCanceledException(cancellationToken)`, the type its caller catches, as their event data holds no exception; the entry removed (F8) | `Error` on `CommandFailed`'s answer |
| Every SQL tracker | the answer under the request's test and phase (F9) | `Error` on every failed answer; a reader that fails partway answered as failed (F10); ClickHouse.Driver answers its reader when it is read (F11) |
| `TrackingProxy` | `ExceptionDispatchInfo.Throw` keeps the stack (F17) | `Error` on its answer; a stream answered when it ends (F24); `500` kept unless Q2 says otherwise |
| gRPC | | `Error` on every failed answer; non-gRPC `500` kept unless Q2 says otherwise |
| MassTransit | | `Error` on the fault call (F23) |
| Elasticsearch | | a call whose response holds an `OriginalException` answered `!` and its type, with `Error` (F22) |
| Kafka, ProxyTap, MongoDB, gRPC | onto `OpenCall`; records unchanged | |
| `DeferredLogFlushHandler` | the flush in a `finally`, so the calls queued during a send that threw go to its test (F28) | |

R1 moves only calls that had no answer, and the calls F28 filed under another test. R2 changes the status of an answer only where it had none (Pub/Sub, Event Hubs, Service
Bus, Elasticsearch) or claimed success (F10, F11, F12, F24). Everywhere else it adds `Error` and leaves the status alone, so the
fingerprint does not move.

### 5.4 One reading of how a call ended (R2)

`InteractionStatus` (`src/Kronikol/Reports/InteractionStatus.cs`) gains the one reading: an answer is failed when `IsError`
says so or it carries an `Error`; a request with no answer is unanswered. Every reader in §2.3 takes it.

- **`Failures.md` and `Failures.jsonl`:** an unanswered call reads `no answer` in the status column (F27), and a failed answer's
  reason line comes from `Error`, which every failed answer now has.
- **`query flow --errors-only` and `query failures`:** both take the reading. Neither prints anything new.
- **The component diagram:** `% errors` counts every failed answer, not only numeric codes (F19). Its numbers rise wherever
  calls threw. That is a fix, and it goes in the changelog.
- **OTLP export:** the message is `Error`, which a SQL failure now has (F21).

No reader's output gains a field: `query failures --json` already writes `error` beside its numeric `status`
(`QueryCommand.Narrative.cs:272-273`), which is enough to tell a failure apart.

**How it meets #140.** #140 asks for a way to call a custom tracker's text status a success, as `IsError` reads `Completed` or
`Published` as failed (`InteractionStatus.cs:84-88`). Once C2 holds, every failure a Kronikol tracker writes carries `Error` and a
status the reading names: a `!` status, a code of 400 or more, or the SQL family's and Spanner's `Error`. So the reading tries
those first, and the text rule is left for what custom trackers write, which is #140's case. #140 can then add its success flag,
a public member and so a minor, without moving any built-in tracker's reading. R2 documents today's list on
`Tracking-Custom-Dependencies.md` (F29, Q11).

### 5.5 The diagram draws why (R3, only on the owner's word)

The return arrow of a failed answer gets its reason as a note, beside the note it may already have, at every verbosity the call
is drawn at with notes. It is a new diagram feature, so R3 is a minor, and it waits for Q10. It touches the note machinery
(`PlantUmlCreator.cs:1221-1228`) that kronikol-8b's #227 plan reads in the browser (`collapsible-notes-script.js`), so its
order against that plan is agreed first. `LogResponseContent = false` does not hide it: that option keeps bodies out of notes
(`Integration-CosmosDB-Extension.md:357`), and a reason is not a body. Redaction already covers `Error` (`CaptureRedaction.cs:59`,
`:95-97`).

### 5.6 History: one rule

R1, and R2 if Q1 folds it in, moves the fingerprint of every scenario that has a failing call of a moved tracker. The bridge is
#125's, with one change:

- `InteractionShape.Version` goes to 12, and 11 joins `KeptVersions` (`InteractionShape.cs:72`, `:78`).
- An answer that R1 adds carries `RecordedBefore12 = RecordedBefore.NotRecorded`, passed by its tracker (§5.2), which rules 6 to
  11 read as no answer (the chains at `InteractionShape.cs:369-412` gain `RecordedBefore12` last).
- An answer that R2 gives a status where it had none (Pub/Sub, Event Hubs, Service Bus, Elasticsearch) carries the same mark.
  Under every older rule an answer with no status and no answer end their line alike, with `-` (`InteractionShape.cs:19`): a
  call marked unanswered only loses its status (`:285`, `:296`, `:306`, `:318`).
- **The change:** only an answer whose status moves from one value to another needs more: F10, F11, F12 and F24, which claimed a
  success, and the made-up `500`s if Q2 says yes. `RecordedBefore` today holds a method, a URI and `Absent`
  (`RequestResponseLog.cs:216-219`), so it gains the answer's status then. `HistoryRawCall` gains the status under 11, and
  `HistoryJson` names it, so a fragment carries it (`history record`) as it carries `Unanswered8`. Every rule from 6 to 11 reads
  the status rule 11 read.
- A call TcpTap now answers (Q5) is new as a whole: its request carries the mark, which older rules read as no call (`Absent`).
- **What no mark bridges:** F28 moves calls between scenarios. The flush after a send that threw marks what it writes, so the
  scenario that made the calls keeps its older fingerprints. The scenario that got them loses calls it no longer has to mark.
  It was whichever scenario sent next, which already varied from run to run, so each such scenario reads changed once at most.

If R2 ships apart from R1 (Q1 answered no), R2 takes rule 13 the same way.

### 5.7 The guard (R1)

- **A contract fact in every tracker's test project**, with `[Trait("Contract", "FailedCall")]`. It drives the shipped tracker
  over an inner client that throws, for every entry point the tracker tracks, and asserts one request and one answer, the answer
  C2 and C3 describe, and `Assert.Same` on the exception at the tracker's own boundary, with its stack still naming the inner
  frame. Where an SDK client sits above the tracker, the fact also drives it through the client, as the probe does, and asserts
  the answer only.
- **A guard fact in `Kronikol.Tests`**, from the source. Every `src/Kronikol.Extensions.*` project that logs a request has a test
  project holding such a fact. And every `RequestResponseLog` of type `Request` built under `src/` goes through `OpenCall.Open`, or is on a list
  with its reason. At 5.31.2 they are built in 35 files. The list: the markers (`TrackingDiagramOverride.cs`), ingest's
  `InteractionRecord.ToLog`, `RequestResponseLogger.LogPair` and the deferred queue's flush (`PendingRequestResponseLogs.cs`),
  the trackers that write both halves at once after the outcome (`TrackingProxy` through `LogPair`, the OTLP tap's
  `MappedSpan.cs`, TcpTap, Elasticsearch's callback, MassTransit's three observers), and the public trackers whose halves a
  consumer calls (`MessageTracker`, `BigQueryTracker`, `BigtableTracker`). The diagram, the merge and the flow builders name
  `RequestResponseType.Request` without building a record, so the fact reads constructions, not the name. A new tracker that builds a request any other way fails the build until it is moved or listed.

### 5.8 Rejected

- **The issue's edit, Storage Queues' catch copied into three more handlers.** It answers three of the thirteen paths that §2.1
  and F1 to F9 find unanswered, and keeps the cause (§4).
- **A reader that calls every unanswered request failed.** A request with no answer is also a call still running when the test
  ended, or a stream never read (C1's exception), or a tracker's bug, and the record cannot say which. The guess would mark
  successes failed. R2 names it `no answer`, which is true.
- **Answering in `Dispose`.** A `using` block cannot see the exception: F13 is that mistake.
- **`AppDomain.FirstChanceException`.** It also sees every exception the client catches itself.
- **Changing the public `LogResponse` methods** (`PubSubTracker.cs:86`, `EventHubsTracker.cs:79`, `BigQueryTracker`,
  `BigtableTracker`). A breaking change; they stay, and Q3 asks about a failure method beside them.
- **A rule per package**, as #125 took two. Every kept rule costs a function, pins and a replay, so one change takes one rule.

### 5.9 What does not change

- A call that succeeds: its records stay byte for byte, as the existing goldens pin.
- An HTTP 4xx or 5xx answer: it is an answer, and stays as it is.
- Shape A's writers: they move onto `OpenCall` with identical records.
- The data file and the ingest contract: `error` has existed since 3.18.0 (`Generated-Reports.md:423`), and nothing is added.
- `Raw` verbosity records.

## 6. Tests

Every fact is written first and seen red on the published 5.31.1 package, in a worktree at `v5.31.1` (CLAUDE.md).

### 6.1 `OpenCall` (core, `tests/Kronikol.Tests/Tracking/OpenCallTests.cs`)

- **`Threw`:** writes `FailedSend`'s shape, and every field §5.2 copies equals the request's.
- **The variants:** they keep their method and URI and lose body and headers.
- **Once:** a second answer is dropped, after `Answer` or after `Threw`.
- **C5:** the answer goes to the request's test and phase after the current test and phase have changed.
- **C4:** the exception is untouched, the same object with the same top frame.
- **No mark:** `Threw` writes no `RecordedBefore*` of its own, and keeps what `adjust` sets.

### 6.2 The contract facts (one class per tracker's test project)

Red on 5.31.1: SQS, SNS, EventBridge, S3, DynamoDB, Atlas Data API, BigQuery (the body read), Cosmos DB (the variants), Redis
(the answer and the exception's type), Pub/Sub (the subscriber), Event Hubs (the reader), Spanner (the interceptor), EF Core (a
cancelled command), Sqlite (a test that changes between request and answer, F9), `TrackingProxy` (the stack), `DeferredLogFlushHandler` (F28). Green on 5.31.1,
kept as pins: Storage Queues, Blob Storage, Cloud Storage, the HTTP handler, Kafka, ProxyTap. R2 adds its facts for F10 to F12,
F14, F21 to F24 the same way.

### 6.3 The guard fact (`tests/Kronikol.Tests/Tracking/FailedCallGuardTests.cs`)

Red against a scratch tracker that logs a request with `RequestResponseLogger.Log`, and against a test project with the trait
removed. Each list entry is mutation-checked: remove it, and the fact fails naming the file.

### 6.4 History

- **A replay per moved package:** a `HistoryAcrossTheFixTests` in each moved package's test project, as Storage Queues has
  (`tests/Kronikol.Tests.StorageQueues/HistoryAcrossTheFixTests.cs`). Runs 1 to 4 are recorded as the published 5.31.1 records
  them, from a frozen record that is never edited, and runs 5 to 8 by the new build. The verdicts equal those of the new build
  throughout, scenario by scenario, and one real change at the upgrade still reads changed.
- **The pins** that every rule move has needed: `ShapeGuardTests` `Pins[12]`, the 37 `HistoryGuard.Pin(` lines, the card
  goldens' `"operationKey"`, `HistoryBridgeTests`, `HistoryFoldRuleTests` and `InteractionShapeTests`.
- **The rest:** a fragment round trip that carries the status under 11, and `kronikol merge --history` over a window that holds
  both versions.

### 6.5 The readers (R2)

One theory over every shape of §2.2, R2's shape and no answer, against every reader of §2.3. It pins what each prints.

### 6.6 What is painted (R3)

Playwright facts under `tests/Kronikol.Tests.EndToEnd`, following CLAUDE.md's rules (`PollingInterval = 200`, no forced clicks):

- a failed send's reason is painted, in both themes;
- a success gets no reason note;
- the note folds and unfolds as other notes do.

### 6.7 Proof before each tag

- the new facts red on the previous tag;
- a Linux run of the trackers' projects (podman, as the CI lanes run);
- `release.slnf` built in Release for every target;
- a mutation of each new branch: drop the catch, drop the variants, swap the exception, read the test at the end.

## 7. Releases

Numbers are taken at push, against whatever the peers have pushed by then.

- **R1, a patch: every call ends with an answer.** `OpenCall`; the guard; every unanswered path of §2.1 (F1 to F9), #232's three
  among them; the exception left untouched (F16, F17); history rule 12 with its bridge. No public member is added, and every
  change is a fix, so the patch number moves. The changelog says what a consumer sees: a failed call now draws a return labelled
  with the exception's type, history bridges the move with no quiet run, and Redis's synchronous calls throw what the client
  threw.
  - **Wiki:** `Integration-SQS-Extension.md`, `-SNS-`, `-EventBridge-`, `-S3-`, `-DynamoDB-`, `-AtlasDataApi-`, `-Redis-`,
    `-PubSub-`, `-EventHubs-`, `-Spanner-`, `-EF-Core-Relational-` (what a failed call shows); `Cross-Run-History.md` (rule 12, as
    `:478-498` lists 9 to 11); `HTTP-Tracking-Setup.md:110-120`, which says which trackers answer a call that threw; and
    `Integration-DispatchProxy-Extension.md` (`:105`, the queue flushed after a send that threw too; `:169`, the stack kept).
  - **Kronikol4J's ledger** (`docs/REMAINING_PARITY.md`): an entry for the answers Java does not write, and a correction to
    `:1952-1962`, which says a Java capture leaves a bare request where it leaves nothing.
- **R2, a patch: one shape for a failed answer, and one reading of it.** §5.3's R2 column and §5.4. Fixes, with behaviour a
  consumer sees: Pub/Sub, Event Hubs and Service Bus failures gain a status; `% errors` counts calls that threw; `Failures.md`
  says `no answer`. If Q1 says to fold it in, it ships as R1, under rule 12. Wiki: each changed tracker's page,
  `Tracking-Custom-Dependencies.md` (Q11), and `Integration-DispatchProxy-Extension.md:169` if Q2 says yes.
- **R3, a minor, on the owner's word only: the diagram draws why a call failed.** §5.5. Minor, since a new diagram feature is the
  highest-ranking change in it. The Kronikol4J ledger records the new note.

#232 is closed when R1 is published, with a comment that names R2 and R3 (Appendix A), posted only at the owner's word (Q8).

## 8. Edit sites

- **R1, core:** `src/Kronikol/Tracking/OpenCall.cs` (new); `RequestResponseLog.cs` (`RecordedBefore12`); `FailedSend.cs` (its
  doc points at `OpenCall`); `TestTrackingMessageHandler.cs:292-350` (5.31.2's lines); `TrackingProxy.cs:141-200`;
  `src/Kronikol/Sql/SqlDiagnosticTracker.cs:116`, `:190`, `:293-365`; `History/InteractionShape.cs:59-78`, `:340-440`;
  `HistoryRawCalls.cs`; `HistoryJson.cs`; `DeferredLogFlushHandler.cs` and `PendingRequestResponseLogs.cs` (F28).
- **R1, the trackers:**
  - the six handlers' `SendAsync` (§2.1's lines);
  - BigQuery `:99-137`; Cosmos DB `:84-143`; Storage Queues `:75-143`, Blob Storage and Cloud Storage alike;
  - `RedisTrackingDatabase.cs:69-117`, `RedisTracker.cs:68`, `:141`;
  - `TrackingSubscriberClient.cs:38-50`; `TrackingEventHubConsumerClient.cs:107-140`;
  - `SpannerTrackingInterceptor.cs:45-80`, `:216-224`; `SqlTrackingInterceptor.cs:101`, `:220-229`;
  - the trackers' internal request methods (`ServiceBusTracker.cs:70`, `EventHubsTracker.cs:59`, `PubSubTracker.cs:66`);
  - onto `OpenCall` with records unchanged: `KafkaTracker.cs:410-453`, `ProxyTap.cs`, `MongoDbTrackingSubscriber.cs:128-260`,
    `GrpcTrackingInterceptor.cs`.
- **R2:**
  - `TrackingDbDataReader.cs:95-108`, `:234-242`; `TrackingClickHouseClient.cs:106-171`;
  - `TrackingAsyncStreamReader.cs:42-79`; `SpannerTracker.cs:104`;
  - the brokers' catches (§2.1's lines); `MassTransitTracker.cs:201-236`; `GrpcTrackingInterceptor.cs:410-419`;
  - `ElasticsearchTrackingCallbackHandler.cs:83-109`;
  - `InteractionStatus.cs`; `FailuresDigestGenerator.cs:348`, `:382-391`, `:793-807`; `QueryCommand.Narrative.cs:691`,
    `:1092-1102`; `ComponentFlowSegmentBuilder.cs:203-243`, `:498`; `OtlpSpanMapper.cs:464-492`.
- **R3:** `PlantUmlCreator.cs:1203-1228`, and its browser counterpart if #227 has moved it.

## 9. Risks

- **A wrong bridge** reads every scenario with a failing call as changed once. The per-package replays (§6.4) are the guard.
- **OTLP dashboards:** an unanswered call was an orphan span marked `kronikol.orphan`, and after R1 it is an `ERROR` span with a
  duration. A dashboard counting orphans counts fewer. This goes in the changelog.
- **Diagrams grow by one return per failed attempt.** A retry loop's first attempts become visible. That is the point (3.18.0
  made it for HTTP), and it goes in the changelog.
- **Peers:**
  - #219's plan has since been green-lit. Its R1 is out as 5.31.2, which this plan's citations already read, and its R2, next,
    changes the HTTP handler again. kronikol-70 is executing #138 (`C:/Code/Kronikol-138`), which edits the HTTP handler's `:54`
    and `:149` and the listener calls in `GrpcTrackingInterceptor` and `KafkaTracker`. Those are not the lines R1 and R2 edit,
    but whichever lands second rebases.
  - kronikol-8b's #227 plan touches what R3 touches (§5.5).
  - History rule numbers are taken at push, like versions.
- **F28's move** reads changed once in a scenario that got another's calls (§5.6).
- **Kronikol4J byte parity** breaks for failed calls, as it already does for Shape A; the ledger records it.

## 10. Questions

- **Q1. Ship R1 and R2 as one release, under one history rule?** **Recommended: yes.** Two rules cost two kept functions and two
  sets of pins for one change, and between the two releases the brokers' failures would still read as successes in history.
- **Q2. Should a status a tracker makes up become `!Type`?** These are `TrackingProxy`'s `500`, gRPC's `500` for a non-gRPC
  exception, and MongoDB's `500` for a client-side failure (F25). **Recommended: yes, in R2.** No service sent those 500s, and
  each one moves only the fingerprint of a call that failed, under the same rule, with the status-then mark (§5.6). The wiki
  documents `TrackingProxy`'s (`Integration-DispatchProxy-Extension.md:169`), and that page changes with it.
- **Q3. Make `OpenCall` public, and give `MessageTracker`, `BigQueryTracker` and `BigtableTracker` a failure method?**
  **Recommended: not now.** It is new surface (a minor), and no consumer has asked; `FailedSend` stays public for any other
  capturer.
- **Q4. Kafka's consume scope (F13) needs an API that sees the handler's exception.** An example is a `TrackConsume` overload
  that runs the handler. **Recommended: an issue of its own.** It is a new public member, so a minor.
- **Q5. TcpTap's dropped command (F26):** answer it `!` with the reset? **Recommended: yes, in R2.** The decoder already knows the
  command.
- **Q6. A service's own failure code:** a SQL error number or SQLSTATE, a gRPC status name or a MongoDB code name, in place of
  `Error` or `500`. **Recommended: an issue of its own.** It moves every database failure's fingerprint for a gain no reader has
  asked for.
- **Q7. Kronikol4J:** file the missing answers there (F20)? **Recommended: yes, as an issue,** once R1 is out, at the owner's word.
- **Q8. Close #232 when R1 is published, with Appendix A?** **Recommended: yes,** at the owner's word.
- **Q9. File F10 to F13 and F22 to F26 as issues?** **Recommended: no, if R2 ships.** They are its scope, and only what this plan
  leaves (§11) gets an issue.
- **Q10. R3, the reason drawn on the diagram?** **Recommended: yes, after #227.** Without it the reason stays in `Failures.md` and
  `query`, as today.
- **Q11. #140: document today's success words in R2, and leave its flag to #140?** **Recommended: yes.** R2's reading puts
  `Error` and a `!` status first (§5.4), so #140's flag, a public member and so a minor, then changes no built-in tracker.
  Documenting the list on `Tracking-Custom-Dependencies.md` is a docs fix (F29).

## 11. What it leaves

- Kafka's consume scope (Q4).
- A service's own failure code (Q6).
- Public failure methods for consumers' trackers (Q3).
- Calls no tracker sees: a connection's `Open`, a failed `BEGIN TRANSACTION`, MongoDB's server selection, Service Bus processors,
  Dapper transactions, ClickHouse.Driver's `PingAsync`.
- Kronikol4J (Q7).
- #140's success flag (Q11).

## 12. Log

**2026-11-07, written.**

- **Isolation:** in its own worktree from `896ddad3`, fetched into a private ref (`refs/k00/main`), with the wiki read from its
  own private ref (`refs/k00/master`, `f73bdd6`). No file in the shared checkout or the wiki checkout was changed, and the
  shared `origin/*` refs did not move. The only additions to the shared repositories are those two refs and the worktree's
  registration.
- **Peers:** kronikol-33, kronikol-8b and kronikol-7c were told the paths. kronikol-8b is planning #227 (§5.5); kronikol-7c has
  nothing in flight.
- **Method:**
  - four read-only surveys covered the trackers, in three groups, and their readers. Every `file:line` this plan cites was then
    re-read by hand at `896ddad3`. That corrected two lines the surveys gave (the HTTP handler's transport span, the brokers'
    phase lines) and one claim of the first draft: Elasticsearch, Bigtable and MassTransit are not in core's
    `InternalsVisibleTo` list;
  - the probe in the harness measures the RUN rows against the published 5.31.1 packages. It ran on 2026-11-07, exited 0 and
    printed 525 lines, every case built and none skipped (`results/failed-calls-5.31.1.txt`; the harness README says what
    resolved).

**2026-11-07, checked again at 5.31.2 and deepened.**

- **What moved:** main went from `896ddad3` to `55999241` (5.31.2, #219's R1). Of the files cited only
  `TestTrackingMessageHandler.cs` changed: its catch now opens at `:297`, and the transport span is at `:319`. The changelog
  grew by 24 lines at its top. The wiki (`f73bdd6` to `7f72c19`) changed no page this plan cites, and Kronikol4J's ledger entry
  is now `:1952-1962` (`bd47938`). All three were read through private refs, as before.
- **Measured** (`probes/more-failed-calls.cs`, on the published 5.31.1): the EF Core cancel (F8), a Sqlite reader that fails
  partway (F10) and a `TrackingProxy` stream (F24), each as read. Also a new finding, F28: the deferred calls of a send that
  threw are filed under the next test.
- **Read:**
  - how the older rules read a marked answer (an unanswered call only loses its status), which leaves the status-then mark only
    the moves from one status to another;
  - the history marks each copied catch puts on its failed answer, so `Threw` sets none;
  - the HTTP handler's response focus fields, and that `Error` is redacted like a body;
  - every file that builds a request record (35), for the guard's list;
  - #140, which §5.4 now meets.
  The copies' timeline is exact: Cosmos DB's catch came with the HTTP handler's in 3.18.0, so #232's edit would be the fifth.
- **Changed in the plan:**
  - §0 points 4, 7 and 8;
  - §2.1's HTTP, `TrackingProxy`, EF Core and reader rows, and a deferred row;
  - F8 to F10, F18, F24, F28 and F29;
  - §5.2's marks and `adjust`, and §5.3's EF Core and deferred rows;
  - §5.4's #140, §5.5's note on `LogResponseContent`, §5.6's bridge and §5.7's list;
  - Q2 and Q11.
  The Elasticsearch `InternalsVisibleTo` edit is dropped, as it needs none.

**2026-11-07, R1 and R2 executed as 5.34.3**, at the owner's "implement the plan in full", every question at its recommended
answer: Q1 one patch under rule 12; Q2 `!` and the type, with the status-then mark; Q3 `OpenCall` internal; Q4 and Q6 issues
of their own; Q5 TcpTap answers the close; Q7 a Kronikol4J issue; Q8 #232 closed with Appendix A; Q9 no; Q10 R3 after this
release, its order agreed with #227's session (whichever is ready first goes first); Q11 the success words documented.

- **Corrected while building:**
  - F3 is wider than BigQuery: Cloud Storage, Blob Storage and Storage Queues also read the response body outside their `try`.
    Each now answers a body that breaks off as it is read; a send that throws keeps its `RecordedBefore9` or `RecordedBefore11`
    mark, and a body read that throws takes `RecordedBefore12`.
  - Elasticsearch does need core's internals, for the `RecordedBefore12` mark on its new answer. The edit dropped above is back.
  - ProxyTap writes both halves to its own sink at once, after the forward, so it is on the guard's list (§5.7) rather than
    moved onto `OpenCall`.
  - The SQL family, Spanner, gRPC and MongoDB pass a call's ids between their parts, not the call. They hold an `OpenCalls`
    registry (core, beside `OpenCall`: `Open`, `Find`, `Take`), so every answer still goes through `OpenCall`, once, under the
    request's test and phase, with far less change across the ten SQL packages.
  - F11 cannot be fixed as written: ClickHouse.Driver's reader has only a private constructor, and its factory is internal.
    The call stays answered when the reader is returned, and the code and the wiki say a later read's failure is not seen.
  - The OTLP tap keeps its `500` and gains `Error` (C2): the span's status message, or a sentence saying it had none.
  - `InteractionStatus`'s one reading is internal: a public member would make the release a minor.
  - The guard reads the contract trait written short or in full (MassTransit's fact names it in full), and Bigtable has no
    contract fact, as its tracker is called by hand and writes no failure of its own.
  - gRPC's and MongoDB.V2's test projects gain core's internals, for the rule-numbered reading their history facts use.
- **The bridge, checked against what shipped:** `probes/probe-on-build.py` runs a copy of `failed-calls.cs` on this build
  (`#:project` references in place of the 5.31.1 packages), and `probes/compare-lines.py` compares its lines with the frozen
  `results/failed-calls-5.31.1.txt`: all 45 cases read the same under rule 11 (`results/across-the-fix-232.txt`), and rule 12
  reads the answers §5.3 names. That, and the facts in 21 trackers' projects that read every rule before 12, stand for §6.4's
  per-package replays, which would have frozen the same 5.31.1 lines again.
- **Left:** R3 (a minor, §5.5), the Kafka consume scope (Q4, #242) and a service's own failure codes (Q6, #243), and Kronikol4J
  (Q7, filed there once this release is out).

## Appendix A. The comment closing #232, to post only at the owner's word

> Fixed in {R1's version}. The three handlers now answer a call that throws, with the exception's type as the status and its
> message chain in `error`, as Storage Queues does. The cause was wider than the three: nothing in Kronikol ended a call, so
> each tracker wrote its own answer, or none. A call now ends through one shared piece of code, and a test in every tracker's
> project fails if a call can be left unanswered. The same release answers the calls S3, DynamoDB, Atlas Data API, Redis, a
> Pub/Sub subscriber, an Event Hubs read, Spanner's interceptor and a cancelled EF Core command left alone, and a call that throws no longer
> hands the calls a deferred `TrackingProxy` queued during it to the next test. History bridges the
> move, so the first run after the upgrade reads no change. {If R2 ships with it: Pub/Sub, Event Hubs and Service Bus failures
> now carry a status, so history tells a failed send from a good one.}
