# Research for lemonlion/Kronikol#135 (Kafka trace ids, internal flow, queryable content)

Worktree: `C:/Code/Kronikol-kafka135` at `417c8e58` (Release 4.9.0). All paths below are relative to it.
Issue text (fetched with `gh issue view 135`): three items. 1) `KafkaBuildInterceptor` misses
`DependentProducerBuilder`. 2) produce/consume entries carry no `ActivityTraceId`/`ActivitySpanId`, so `query trace`
cannot follow Kafka and a consume arrow's popup "only finds the consume span itself (the entry is logged at one
instant)". 3) content `Key: k, Value: v` is not queryable; asks for a JSON-envelope option. The reporter's own wrapper
library builds producers with `DependentProducerBuilder`, so they decorate their own interfaces and **call
`KafkaTracker.LogProduce` / `LogConsume` directly** (so a fix that only lives in `TrackingKafka*` wrappers misses them).

Empirical probe (scratchpad `kafkaprobe/`, references the built `bin/Debug/net10.0` DLLs, nothing written to the
worktree):

```
pair: elapsed 0.2294 ms / 0.0051 / 0.0002 / 0.0002 / 0.0002 ms, reqTrace=null, status=null   (5 x KafkaTracker.LogConsume)
same-instant pair, trace+span set: 0 span(s)
response stamped after processing:  2 span(s): process orders, db query
no trace id, late response (test has no traced calls -> unclaimed pool): 2 span(s)
no trace id, late response, test HAS a traced call on another trace: 0 span(s)
```

---

## 0. What KafkaTracker records today (baseline)

- `LogOutgoing` / `LogIncoming` write two records (`src/Kronikol.Extensions.Kafka/KafkaTracker.cs:98-144`, `146-193`):
  fresh `Guid` Kronikol `traceId` and `requestResponseId` per call (`:109-110`, `:157-158`); request carries the content,
  response `Content = null` (`:131`, `:180`); no `StatusCode`; `MetaType: Event`, `DependencyCategory: MessageQueue`
  (`:117-118`); headers always `[]` (`:114`, `:131`, `:163`, `:180`); consume swaps caller/service so the arrow is
  Kafka -> app (`:160-165`). No `Timestamp`, no `ActivityTraceId`/`ActivitySpanId` set anywhere in the file.
- Timestamp: `RequestResponseLogger.Log` stamps `DateTimeOffset.UtcNow` when null (`src/Kronikol/Tracking/RequestResponseLogger.cs:53-54`).
  The two halves are logged by two `Log` calls, so they get **two different instants a few microseconds apart, not
  one**. The data file's `durationMs` is `elapsed > 0 ? elapsed : none` (`src/Kronikol/Reports/ReportGenerator.cs:4634-4643`),
  so Kafka calls get a spurious positive duration (probe: 0.0002-0.23 ms) that `query` prints as `0 ms`
  (`src/Kronikol/Query/QueryWriter.cs:520-521`). `LogPair` was fixed in 4.7.3 to stamp one instant and write no
  duration (`RequestResponseLogger.cs:106-110`; guarded by `tests/Kronikol.Tests/Tracking/RequestResponseLoggerTests.cs:19-45`);
  KafkaTracker (and MassTransitTracker) still have the defect that fix addressed.
- Consumer: `Consume(...)` calls the inner consumer, then `LogConsumeResult` (`src/Kronikol.Extensions.Kafka/TrackingKafkaConsumer.cs:24-43`, `70-81`),
  i.e. the pair is logged right after `Consume` returns, before the app processes the message.
- Producer: `ProduceAsync` logs after the awaited delivery (`src/Kronikol.Extensions.Kafka/TrackingKafkaProducer.cs:24-37`).
  Sync `Produce` builds content at call time (`:60`) but calls `LogProduce` **inside the delivery handler** (`:62-68`),
  which Confluent runs on the producer's poll thread, so `TestInfoResolver` (and any `Activity.Current` read there)
  sees the poll thread's context, not the caller's (see Adjacent defects).
- Content: `$"Key: {message.Key}"`, `$"Value: {message.Value}"` joined by `", "` (`TrackingKafkaProducer.cs:92-102`,
  `TrackingKafkaConsumer.cs:119-130`), gated by `LogMessageKey`/`LogMessageValue` (`src/Kronikol.Extensions.Kafka/KafkaTrackingOptions.cs:23-24`).
  `ToString()` of `TValue`: a string as-is, `byte[]` as `System.Byte[]`, a generated protobuf message as its JSON
  (Google.Protobuf's generated `ToString()` calls `JsonFormatter.ToDiagnosticString`), an Avro record as whatever its
  `ToString` gives. The extension references only `Confluent.Kafka` + core (`src/Kronikol.Extensions.Kafka/Kronikol.Extensions.Kafka.csproj`).

---

## 1. Internal flow: how spans are attributed to an arrow

Wiring: `ReportGenerator` builds one span list (ingest-supplied `FlowSpan`s or this process's activities via
`InternalFlowSpanCollector.CollectSpans` -> `FlowSpan.From`) and calls `InternalFlowSegmentBuilder.SegmentsOf` and
`WholeTestSegmentsOf` (`src/Kronikol/Reports/ReportGenerator.cs:329-350`). Defaults: `InternalFlowTracking = true`,
granularity `AutoInstrumentation`, `InternalFlowNoDataBehavior = HideLink`, whole-test flow `Both`
(`src/Kronikol/ReportConfigurationOptions.cs:173`, `187`, `193`, `218`).

**It is all of: trace id, span tree, time window, and test id.** `InternalFlowSegmentBuilder.Attribute`
(`src/Kronikol/InternalFlow/InternalFlowSegmentBuilder.cs:122-273`):

1. Only logs with a `Timestamp` take part (`:132`); only request records that are calls (not markers/user actions) get a
   segment, keyed `iflow-{RequestResponseId}` (`:196-204`, `:238`, `IsCall` `:328`).
2. **Trace claim**: per `ActivityTraceId`, the set of tests whose calls record it (`:133-140`). Request and response
   records both count (any timed call record).
3. **Candidate pool** (`:223-225`):
   - call has `ActivityTraceId` -> `ByTraceAndTree(trace, ActivitySpanId)` (`:152-162`): the trace's spans; but if calls
     of **more than one test** record that trace and the call has a span id, the **subtree** rooted at that span id
     (the span itself if stored, plus descendants by `ParentSpanId`, `Subtree` `:279-308`), falling back to the whole
     trace if the subtree is empty (`:158-161`).
   - no trace id, but the test has traced calls -> `testSpans`, the union of what the test's traced calls may take (`:168-178`).
   - no trace id and the test has no traced call -> `unclaimed`, spans of traces no call records (`:143`).
4. **Time window**: start = request timestamp; end = the matching response's timestamp by `RequestResponseId`
   (`:182-187`, `:213-214`), else the test's next record's timestamp, else +5 s (`:215-218`). A span is kept if
   `request - 50 ms <= span.Start < end` (`:230-236`). Span end is not considered.
5. **Cross-test dedupe**: a span selected by calls of two tests goes to neither and is counted as `SpansLeftOut` (`:242-266`).
6. Before all this, `AutoInstrumentation` granularity drops every trace that holds no span of a well-known source
   (`src/Kronikol/InternalFlow/InternalFlowSpanCollector.cs:73-90`; list `:11-27`, which has `Kronikol.Grpc` and
   `Grpc.Net.Client` but **no Kafka source**). Spans are captured only once a listener runs: `InternalFlowActivityListener.EnsureStarted`
   is called by `TestTrackingMessageHandler` (`src/Kronikol/Tracking/TestTrackingMessageHandler.cs:130-134`) and the gRPC
   interceptor (`src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs:328-333`), or `AddActivityListenerForInternalFlowTracking`
   (`src/Kronikol/InternalFlow/InternalFlowServiceCollectionExtensions.cs:31-43`); the Kafka extension never starts it,
   and `EnsureStarted` is `internal` (`src/Kronikol/InternalFlow/InternalFlowActivityListener.cs:64`) with no
   `InternalsVisibleTo` for the Kafka assembly (`src/Kronikol/Kronikol.csproj:13-23`). The listener ignores
   `System.Net.Http` (`InternalFlowActivityListener.cs:33-36`, `:48`).
7. Popup data: a segment with spans gets title/content; one with none is omitted under `HideLink`, so the arrow shows no
   link (`src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:161-236`). Every request arrow carries the link markup
   `[[#iflow-{id} ...]]`, Event arrows included (`src/Kronikol/PlantUml/PlantUmlCreator.cs:368-377`).
8. Whole-test flow: every span of each trace claimed **only** by this test's calls, **not time-filtered**, plus the
   spans its calls kept (`InternalFlowSegmentBuilder.cs:67-80`). So a Kafka entry carrying the handler's trace id would
   already put the handler's spans into the scenario's whole-test flow, but not into the arrow popup.

**Today's Kafka (no `ActivityTraceId`)**: pool = `testSpans` (if the test has any traced call, e.g. an HTTP call) or
`unclaimed`; window = `[consume - 50 ms, consume + a few us)`. So the popup can only hold spans of a trace the test's
other traced calls claim (or, with no traced calls, of an unclaimed trace) that **started in the 50 ms before the
consume was logged**, which is exactly "the consume span itself" (an instrumentation receive/process span started just
before `LogConsume`). The handler's spans start after the log, so they are outside the window whatever the trace. There
is no fallback to a wider time window. Probe: same-instant pair with the right trace and span id -> 0 spans; same pair
with the response stamped after processing -> both spans; no trace id + late response -> found only when the test has
no traced call (unclaimed pool); with a traced HTTP call on another trace -> 0 spans.

**What a Kafka consume entry needs for the popup to show the handler's spans** (all three):
- (a) `ActivityTraceId` = the trace the handler's spans are in. In the plain `while(true){ var r = Consume(); Handle(r); }`
  pattern the processing span does not exist yet when `Consume` returns, so `Activity.Current` there is the loop's
  ambient activity (often none, or a stale one inherited by a hosted service; `DetachedHostedService` detaches test
  identity but not `Activity.Current`, `src/Kronikol/Tracking/DetachedHostedService.cs:42-44`). The reliable source is
  the message's propagated context (a `traceparent` header), which nothing in Kronikol writes or reads for Kafka (Q6).
  For direct `LogConsume` callers inside their own processing span (the reporter), `Activity.Current` is right.
- (b) `ActivitySpanId` naming an ancestor of the handler spans (the producer span from `traceparent`, or the processing
  span) - required only when the trace is shared by calls of several tests (subtree path, `:154-160`); otherwise the
  whole trace is the pool.
- (c) A window that covers the handler's work: the response record must be stamped after processing (or the request
  before processing starts and the builder must stop capping at the response). Options: defer the consume's response to
  the consumer's next `Consume`/`Commit`/`StoreOffset`/`Close` (or a scope API for direct callers); or a builder rule
  (e.g. a call whose span id anchors a stored subtree takes the subtree whatever its time). Note a deferred response
  also changes the diagram (handler calls drawn between the consume arrow and its return) and `flow` nesting
  (`CallNesting` nests the handler's calls under the open consume because the consume's service is their caller,
  `src/Kronikol/Query/CallNesting.cs:62-76`), and `durationMs` becomes processing time.
- (d) Under `AutoInstrumentation`, the handler trace must contain a well-known source span, or it is dropped before
  attribution (`InternalFlowSpanCollector.cs:73-90`). Propagating the producer's trace (ASP.NET Core server span) into
  the consumer satisfies this; a Kronikol-owned `Kronikol.Kafka` source added to the list, as `Kronikol.Grpc` is, would too.

Produce arrow: logged after delivery, so its window is the 50 ms before delivery completed; recording
`Activity.Current` at the call (must be captured before the `await`/callback) puts it on the caller's trace.

`SuppliedSpanDiagnostics` (`src/Kronikol/InternalFlow/SuppliedSpanDiagnostics.cs:31-61`) only runs for ingest-supplied
spans (`ReportGenerator.cs:344-345`); an in-process run gets no diagnostic for an unjoined call.

---

## 2. `query trace` and other readers of the W3C ids

`trace` (`src/Kronikol/Query/QueryCommand.Trace.cs`):
- Candidate ids come from **`Request.ActivityTraceId` only** (`:24-28`); the response half is never read.
- Argument: an address (`s3/i47`, errors if the call has no id, `:46-51`), or >= 8 hex chars as a trace prefix
  (`:58-65`, case-insensitive), or a 16-hex span id matched against `Request.ActivitySpanId` (`:72-78`).
- Chain = every request (in a scenario) whose `ActivityTraceId` equals it, `OrdinalIgnoreCase` (`:97-99`); ordered by
  request timestamps, falling back to file order if any is missing/unparseable (`:116-123`); a trace spanning several
  scenarios prints a leak warning (`:101-104`); footer says parent span ids are not captured (`:144`).
- So a Kafka entry needs `ActivityTraceId` (and ideally `ActivitySpanId`) **on the request record**, equal to the
  producer-side trace for produce and to the propagated trace for consume, a timestamp, and to be attributed to a
  scenario: background calls are written under `background.interactions` (`src/Kronikol/Reports/ReportGenerator.cs:4428-4439`)
  and the scanner indexes interactions only under a scenario's `httpInteractions` (`src/Kronikol/Query/ReportScanner.cs:402-452`).
  A consume attributed to another scenario triggers the leak warning.

Other readers:
- `http <addr>` prints `trace <id> span <id>` for whichever half was addressed (`src/Kronikol/Query/QueryCommand.Payloads.cs:329-330`),
  so setting ids on both halves (as HTTP/MessageTracker/gRPC do) keeps `http` on a response consistent.
- `CallNesting` (used by `flow`, `src/Kronikol/Query/QueryCommand.Narrative.cs:523`) **does not read
  `activityTraceId`** (`src/Kronikol/Query/CallNesting.cs:8-24`); it nests by open-request + caller/service and by
  Kronikol's own `traceId` Guid (`:62-76`). KafkaTracker mints a new Guid per call, so a consume never nests under its
  produce by that rule either.
- `ReportIndex.InteractionEntry` holds both as plain strings (`src/Kronikol/Query/ReportIndex.cs:254-255`);
  `interactions --json` does not emit them (`QueryCommand.Payloads.cs:128-144`).

Serialization / format validation:
- JSON: `ActivityTraceId = log.ActivityTraceId`, `ActivitySpanId = log.ActivitySpanId` written verbatim, null included
  (`src/Kronikol/Reports/ReportGenerator.cs:5015-5016`, class `:5053-5054`); XML element only when non-null (`:5347`),
  YAML only when non-null (`:5744-5747`). Schema: `["string","null"]`, no pattern (`:6756-6757`); XSD `xs:string`
  `minOccurs 0` (`:6951`).
- Read back: `ReportScanner` `reader.GetString()` with no check (`src/Kronikol/Query/ReportScanner.cs:755-756`);
  mergeable reader likewise (`src/Kronikol/Reports/Merge/MergeableReportReader.cs:535-536`).
- Ingest: `InteractionRecord.LowerHexOrNull` only trims and lower-cases, blank -> null, **no length/hex check**
  (`src/Kronikol/Ingestion/InteractionRecord.cs:328-329`, `:337`).
- The only 32/16-hex, non-zero check in the codebase is the OTLP exporter's `NormalisedHex`
  (`src/Kronikol.Extensions.Otlp/OtlpSpanMapper.cs:280-305`), which uses a captured id as the exported span's own id.
- Case matters for internal flow: trace ids are joined with `StringComparer.Ordinal` (`InternalFlowSegmentBuilder.cs:108`, `:126`)
  and `FlowSpan` documents lowercase hex (`src/Kronikol/InternalFlow/FlowSpan.cs:16-17`); `query trace` ignores case.
  A `traceparent` parsed from a header must be lower-cased (ProxyTap's parser does, `src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs:183-195`).

---

## 3. `query values --path` and how JSON content is treated

`values` (`src/Kronikol/Query/QueryCommand.Values.cs`):
- Evaluates **response** bodies by default; `--request` or `--both` for requests (`:48-49`). Kafka's content is on the
  request half and the response has none, so without `--request` every Kafka call counts as "carried no body"
  (`:130-133`, `:181-182`). `--where` likewise targets the response unless `req:` or `--request`
  (`src/Kronikol/Query/WhereClause.cs:14-16`, `src/Kronikol/Query/QueryCommand.Shared.cs:157-172`).
- A body is parsed whole with `JsonDocument.Parse` (`src/Kronikol/Query/BodyCache.cs:37-55`); non-JSON (such as
  `Key: k, Value: v`, or `Key: order-1, Value: {"amount":99}`) is counted as "not JSON - counted, not evaluated"
  (`Values.cs:137-142`, `:185-186`). There is no text/regex fallback.
- Path walking (`src/Kronikol/Query/PathEngine.cs:113-172`): a name segment matches only an `Object` element (`:169`).
  So in `{"key":"k","value":"{\"a\":1}"}`, `$.value.a` selects nothing (counted `(absent)`) and `$.value` returns the
  string. **An envelope must embed the value as raw JSON** (object/array/number), not as a string, to be reachable.
  The same is true today of SQS's `MessageBody` (JSON-in-a-string in the SDK's request body).
- Capture-time truncation (`RequestResponseLogger.MaxContentLength`) appends `\n\n…truncated (N chars total)`
  (`RequestResponseLogger.cs:48-49`), which makes an envelope unparseable for `values` (flagged as truncated, `Values.cs:135-136`, `:189-190`).

Other surfaces:
- `body` / `http --body`: `EmitPayload` pretty-prints JSON, leaves text as is (`src/Kronikol/Query/PayloadReader.cs:236-247`,
  `QueryCommand.Payloads.cs:440-500`); `--keys` says "(not JSON - N of text)" (`PathEngine.cs:230-271`); `--path`
  says "the body is not JSON" (`QueryCommand.Payloads.cs:506-525`). `body` on a Kafka response points at the request
  ("carried no body. Its answers s/i did", `:404-409`).
- `grep`: raw case-insensitive substring over every distinct body (`src/Kronikol/Query/QueryCommand.Search.cs:117-141`),
  so `Key: k` text is found; `--values` maps hits to JSON paths only for JSON bodies, else falls back to an excerpt
  (`:129-136`, `PathEngine.cs:274-309`). For JSON-in-a-string it reports `$.value = "{\"a\"…"`, not the inner path.
- `note`: returns the note as drawn from the PlantUML source (already formatted), with a footer that it is not the
  captured content (`QueryCommand.Payloads.cs:572-619`, `PayloadReader.cs:254-302`).
- Diagram note renderer: `FormatNoteContent` tries GraphQL (requests), then `TryFormatAsJson`, which needs the content
  to start with `{` or `[`, indents it and **drops null properties** (`src/Kronikol/PlantUml/PlantUmlCreator.cs:1455-1536`,
  `:1540-1556`, `WriteElementWithoutNulls` `:1778-1790`), then truncated-JSON re-indent; anything else is plain text
  (`:1495-1497`). Event notes get the `<<eventNote>>` class (blue, `:472-473`, `:1257-1269`). JSON-in-a-string is shown
  escaped inside the outer object.
- Browser JSON/YAML toggle: per note, offered only when the note text reconstructs to valid JSON (`JSON.parse` gate,
  `src/Kronikol/Reports/collapsible-notes-script.js:1017-1067`); default start format YAML since 4.0.0
  (`ReportConfigurationOptions.cs:519-524`, enum `src/Kronikol/Reports/NotePayloadFormat.cs`). Today's Kafka text is
  never eligible; a JSON envelope would be, and would start in YAML.

---

## 4. Status, response label, and the other message extensions

Status:
- `InteractionStatus.Split(null)` -> `(null, null)` (`src/Kronikol/Reports/InteractionStatus.cs:29-34`); data file
  writes `statusCode: null`, `statusText: null` (`ReportGenerator.cs:5002-5003`). `IsError(null, null)` is **false**
  (`InteractionStatus.cs:101-112`). Non-error words: `Created, Accepted, NoContent, Sent, Ack, Responded, Hit, Miss,
  Committed` (`:84-88`); **any other text is an error**, so a new label such as `Persisted`/`Delivered` would read as a
  failure in `services`, `flow --errors-only`, `--group-by` and the failures digest unless added there.
- Diagram: the response arrow label is the title-cased status, empty when null (`PlantUmlCreator.cs:1156-1170`,
  `EscapeCapturedLabel("")` returns "" `:1035-1037`). So a Kafka return arrow is drawn **dashed with no label** and no
  note (content null, `:1173`). `flow` prints no status field (`src/Kronikol/Query/QueryCommand.Narrative.cs:555-560`),
  `trace`/`interactions` print an empty status column.

Message extensions that exist: Kafka, MassTransit, ServiceBus, EventHubs, PubSub, SQS, SNS, EventBridge, StorageQueues,
plus core `MessageTracker` (and MediatR, in-process via `TrackingProxy`). **No RabbitMQ, NServiceBus or Rebus.**

| Tracker | Content | Response status | Timestamps | Activity ids |
|---|---|---|---|---|
| Kafka | `Key: …, Value: …` text via `ToString` | none | two `Log` stamps after the op, us apart | none |
| `MessageTracker` (core) | `JsonSerializer.Serialize(payload)` (`src/Kronikol/Tracking/MessageTracker.cs:119-121`, `176-180`, `257-259`) | `Responded` default (`:166`), `Sent` (`:300`), `Ack` (`:326`) | explicit `UtcNow` per half; `TrackSendMessage` one instant (`:260`, `:281`, `:307`) | `Activity.Current` on both halves (`:144-145`, `:203-204`, `:282-283`, `:308-309`) |
| MassTransit | `JsonSerializer.Serialize(message, type)` (`src/Kronikol.Extensions.MassTransit/MassTransitTracker.cs:98-100`, `149-151`) | none; `Fault` on faults (`:228`) | two `Log` stamps after the fact; consume at `PostConsume` (after the handler) | none |
| ServiceBus | `message.Body.ToString()` (BinaryData UTF-8) (`src/Kronikol.Extensions.ServiceBus/TrackingServiceBusSender.cs:135-148`, receiver `:235-248`); batches `[N messages]` | none; a failure is `ex.Message` as content, no status (`TrackingServiceBusSender.cs:44-47`) | request before op, response after | none |
| EventHubs | `EventBody.ToString()`; a batch `[a, b]` (`src/Kronikol.Extensions.EventHubs/TrackingEventHubProducerClient.cs:134-139`); consumer `Events: N` in Raw (`TrackingEventHubConsumerClient.cs:55-56`) | none | request before, response after (whole enumeration for `ReadEventsAsync`) | none |
| PubSub | `Data.ToStringUtf8()` (`src/Kronikol.Extensions.PubSub/TrackingPublisherClient.cs:38-39`; subscriber `TrackingSubscriberClient.cs:42-43`) | none; `Ack`/`Nack` and `MessageId:` only as Raw-verbosity **content** (`TrackingSubscriberClient.cs:48-50`, `TrackingPublisherClient.cs:45-46`) | subscriber **wraps the handler**: request before, response after (`TrackingSubscriberClient.cs:32-56`) | none |
| SQS / SNS / EventBridge / StorageQueues | the SDK's raw HTTP request/response bodies (e.g. `src/Kronikol.Extensions.SQS/SqsTrackingMessageHandler.cs:71-98`) | HTTP status code (`:134`) | before/after send | none (MetaType Default) |

No extension offers a structured/JSON envelope of key/value/headers option (only `LogMessageKey`/`LogMessageValue`
for Kafka and `LogMessageBody` for MassTransit, `src/Kronikol.Extensions.MassTransit/MassTransitTrackingOptions.cs:24`).
Precedents for "JSON of the payload": `MessageTracker` and MassTransit use `System.Text.Json`; gRPC renders protobuf
with `JsonFormatter.Default.Format(IMessage)`, else `ToString()` (`src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs:391-399`).
Kafka message headers are never recorded (`Headers: []`); recording them in `RequestResponseLog.Headers` instead of the
envelope would get `ExcludedHeaders`, `CaptureRedaction` header redaction (`src/Kronikol/Tracking/CaptureRedaction.cs:22-33`, `:82-89`),
`grep --in headers` and `http --headers` for free.

PubSub's subscriber is the in-repo precedent for a consume whose window covers the handler (it owns the handler).

---

## 5. Which trackers set `ActivityTraceId`/`ActivitySpanId`

Confirmed writers (grep of `ActivityTraceId =` / `Activity.Current`):
- core `TestTrackingMessageHandler`: `Activity.Current` ids, else random ids + injected `traceparent`, on request and
  response (`src/Kronikol/Tracking/TestTrackingMessageHandler.cs:154-170`, `:254-255`, `:289-290`, `:316-317`).
- core `MessageTracker` (above).
- `GrpcTrackingInterceptor`: starts its own `Kronikol.Grpc` activity, captures it, injects `traceparent`, stamps both
  halves, completes the activity into the store (`GrpcTrackingInterceptor.cs:18`, `:60-66`, `:246-271`, `:273-326`, `:335-357`).
- `Otlp` (`MappedSpan.cs:89-90`, `:101-102`), `ProxyTap` (`ProxyTap.cs:403-404`, `:415-416`), `TcpTap` (`TcpTap.cs:630-631`, `:646-647`).
- Plumbing that carries ids: `PendingRequestResponseLogs` (`:18-19`, `:51-52`, `:63-64`), ingest `InteractionRecord`,
  `InteractionMerger` (`:320-321`), `MergeableReportReader`.

Every other tracker records none: Kafka, MassTransit, ServiceBus, EventHubs, PubSub, SQS, SNS, EventBridge,
StorageQueues, AtlasDataApi, BigQuery, Bigtable, BlobStorage, CloudStorage, CosmosDB, DynamoDB, S3, Elasticsearch,
MongoDB (and MongoDB.V2, same source files), Redis, Spanner, Dapper, EfCore.Relational, Npgsql, MySqlConnector,
Oracle, SqlClient, Sqlite, ClickHouse (+ .Client/.Driver/.Octonica), core `SqlDiagnosticTracker`, and `TrackingProxy`
(DispatchProxy and MediatR): it starts its own activities (`src/Kronikol/Tracking/TrackingProxy.cs:101-108`) but logs
with `LogPair` (`:216`) and no ids. `Kronikol.Extensions.OpenTelemetry` only feeds the span store.

---

## 6. traceparent, identity headers, message identity lifetime

- **No Kafka (or ServiceBus/EventHubs/PubSub/MassTransit) code reads or writes `traceparent`.** It appears only in
  `TestTrackingMessageHandler` (inject when no `Activity.Current`, `:165-168`), `GrpcTrackingInterceptor`
  (always inject, `:343-357`), ProxyTap (parse/forward/synthesize, `ProxyTap.cs:178`, `:207-270`; parser
  `ProxyTapOptions.cs:173-204`, public but in the ProxyTap package) and Playwright's `TestTrackingIdentity`.
- Header names: `TestTrackingMessageHeaders.TestName = "kronikol-test-name"`, `TestId = "kronikol-test-id"`
  (`src/Kronikol/Constants/HttpHeaders.cs:19-23`); HTTP ones are `test-tracking-*` (`:6-13`). The producer appends
  them as UTF-8 bytes with `Headers.Add` (`TrackingKafkaProducer.cs:104-114`); the consumer reads `TryGetLastBytes`
  (`TrackingKafkaConsumer.cs:93-96`).
- `SetFromMessage` sets the `AsyncLocal` identity and `FromMessage = true`, no disposable
  (`src/Kronikol/Tracking/TestIdentityScope.cs:172-176`); `ClearMessageIdentity` clears only a message-set identity
  (`:184-190`); `Begin` scopes are untouched. Resolution order: HttpContext headers, detached-flow scope/owner window,
  delegate, scope (`Current`), global fallback, background (`src/Kronikol/Tracking/TestInfoResolver.cs:82-120`).
- Kafka: on each **delivered** message the consumer clears, then sets from headers, then correlates, then logs
  (`TrackingKafkaConsumer.cs:72-80`, `:83-100`). So the identity lasts until the next **delivered** message, not the
  next poll: a `Consume` that returns null (timeout) or a partition EOF returns early at `:72` and keeps the previous
  message's identity. Because `Consume` is synchronous the AsyncLocal change flows to the caller's continuation and any
  task it starts afterwards; it does not flow out of an `async` helper that wraps `Consume`.
- PubSub uses a disposable `Begin` scope around the handler instead (`TrackingSubscriberClient.cs:36-38`, `:61-74`).

---

## Adjacent defects found (outside the three items, worth red tests)

1. KafkaTracker pair stamped twice -> spurious ~0 ms `durationMs` (section 0); same in `MassTransitTracker` (two `Log`
   calls, no timestamps, `MassTransitTracker.cs:105-137`). `LogPair` was fixed for this in 4.7.3.
2. Sync `Produce(..., deliveryHandler)` resolves the test identity on Confluent's poll thread (`TrackingKafkaProducer.cs:62-68`
   -> `KafkaTracker.cs:100`); the existing fake inner producer never invokes the handler
   (`tests/Kronikol.Tests.Kafka/TrackingKafkaProducerTests.cs:296-297`), so the path is untested. Any trace-id capture
   must happen at call time, not in the callback. Also the wrapper always passes a handler (`:62`, `:70`); Confluent
   refuses a handler when delivery reports are disabled (believed; verify).
3. Failed produce/consume is not logged at all: an exception from `ProduceAsync`/`Consume` propagates before the log
   (`TrackingKafkaProducer.cs:30-34`, `TrackingKafkaConsumer.cs:24-28`). ServiceBus/EventHubs/PubSub log a failure as
   response content with no status, so `IsError` calls it a success (`TrackingServiceBusSender.cs:44-47`, `TrackingEventHubProducerClient.cs:57-60`, `TrackingPublisherClient.cs:49-52`).
4. MassTransit `PostConsume` clears the message identity **before** logging the consume
   (`src/Kronikol.Extensions.MassTransit/TrackingConsumeObserver.cs:24-31`, introduced in `6a29f87d`), so the consume
   record cannot be attributed by the message's identity; `TrackingConsumeObserver` has no tests.
5. Kafka message identity survives empty polls (section 6).
6. Issue item 1 confirmed: `KafkaBuildInterceptor` patches only `ConsumerBuilder<,>.Build` and `ProducerBuilder<,>.Build`
   (`src/Kronikol.Extensions.Kafka.BuildInterception/KafkaBuildInterceptor.cs:123-143`); nothing in `src/` or `tests/`
   mentions `DependentProducerBuilder`. `.Tracked()` on any `IProducer` works (`src/Kronikol.Extensions.Kafka/KafkaProducerExtensions.cs:17`).

## Notes for the plan

- Fix in `KafkaTracker` (where direct callers land), capturing `Activity.Current` (or a parsed `traceparent`) and
  stamping both halves; give the public `LogProduce`/`LogConsume` a way to pass ids/timestamps, or an overload taking
  the `Message<TKey,TValue>`.
- A `Kronikol.Kafka` ActivitySource (gRPC pattern) would give the produce a span to inject as `traceparent` and a
  well-known source for the `AutoInstrumentation` filter.
- A new option (envelope, propagation) is public surface: minor bump per CLAUDE.md. Changing `durationMs`/status of
  existing Kafka records is observable behaviour: patch, call it out.
- Docs: wiki `Integration-Kafka-Extension.md` documents the `Key: …, Value: …` format (lines 250-254) and
  `Internal-Flow-Tracking.md` step 3 documents the window rule (line 17); `Querying-Reports.md` for `values --request`.
