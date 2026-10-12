# Review C: KAFKA_INTERCEPTION_AND_TRACE_PLAN.md, sections 4.2 to 4.6 and the tests in 5.1 to 5.3

Reviewed against 4.14.6 (`d59843fa`, worktree `C:/Code/Kronikol-audit135`, untouched), the conventions of 4.12.1, 4.12.2,
4.13.0, 4.13.1 and 4.14.3, issues #160 and #167, and the third-party sources in the scratchpad (Confluent.Kafka 2.15.1
and 2.16.0-RC1, OpenTelemetry contrib Confluent.Kafka instrumentation, Confluent.Kafka.Extensions.Diagnostics
("vhatsura"), the runtime's `Activity.cs`).

Labels: **RUN** = measured by the probe below; **READ** = read in source at the cited line; **INFERRED** = reasoned,
not measured.

Probe: `audit/C/probe/Program.cs` (net10.0, references the worktree's built 4.14.6 `Kronikol.dll` and
`Kronikol.Extensions.Kafka.dll` copied into `probe/lib`, Confluent.Kafka 2.16.0, the version the package floats to
today). Output: `audit/C/probe-output.txt`. bin/obj deleted.

---

## H4. F9's `Headers.Remove` and R1's injection both write to the caller's `Message` — CONFIRMED

**Evidence.**
- Plan 4.2 (F9): "removes any it added before (`Headers.Remove`)"; plan 4.3: "a message that already has one is not
  touched" and the produce records "the message's `traceparent` after the inner call". On a reused `Message`, produce 2
  finds produce 1's `traceparent`, leaves it, sends it, and records produce 1's trace and span (READ, follows from the
  plan's two rules). The consumer of message 2 then joins test 1's trace, which is the cross-test claim H7 shows is
  harmful.
- The wrapper cannot tell "its" headers from the caller's: `Headers.Remove(key)` is `headers.RemoveAll(a => a.Key ==
  key)` (Confluent `Headers.cs:152-153`, READ), so a caller's own identity headers are deleted. HTTP (4.14.3,
  `TestTrackingMessageHandler.cs:223-230`) and gRPC (4.12.1/4.13.0, `GrpcTrackingInterceptor.cs:590-628`) both keep a
  header the caller set. The plan's identity rule ("latest wins") and its traceparent rule ("first wins") also
  contradict each other on the same message.
- Today the inner producer is handed the caller's object and the caller's message grows by a pair per produce (RUN
  P7: `kronikol-test-name=Test 1, …, kronikol-test-name=Test 2, …`; "handed the caller's object both times: True").
- Parallel tests producing one shared `Message` (a static template) mutate one `List<IHeader>` while Confluent marshals
  it by index (`Producer.cs:318-325`): Add plus Remove makes that race worse (INFERRED).

**What a copy changes.**
- Success: nothing for the caller. Confluent builds a new `Message` for `DeliveryResult.Message` and
  `DeliveryReport.Message` (`Producer.cs:979-985`, `:1031-1039`, READ; RUN P4c: "report.Message is the caller's object:
  False").
- Failure: `ProduceException.DeliveryResult.Message` is the object Confluent was given (`Producer.cs:767-772`,
  `:786-791`, `:840-845`, `:890-944`; RUN P4a: "the caller's object: True"), and so is the result with delivery reports
  off (`:829-833`). With a copy they hold the copy. A retry that re-sends `ex.DeliveryResult.Message` re-sends
  Kronikol's headers, which add-if-absent then keeps (the same test and trace, which is right for a retry).
- `Headers` null: pass the caller's object through untouched when nothing would be added. Otherwise copy `Key`,
  `Value`, `Timestamp` (Confluent rejects any timestamp other than `Default`/`CreateTime`, `Producer.cs:290-295`, so it
  must be copied as is), a new `Headers` holding the caller's `IHeader`s (immutable, shareable), then add each Kronikol
  header only if absent. An inner instrumentation wrapper then writes into the copy, which Kronikol holds and can
  read.
- Mocks: a user's mocked inner producer that matches the message by reference stops matching (INFERRED, niche).
- #136's facts and the other header facts all read the caller's message:
  - `TrackingKafkaProducerTests.cs:221`, `:255`, `:274` (#136) and `:292` (#136) fail under a copy. The `FakeProducer`
    (`:308-337`) discards what it receives, so the shared fake must record the inner message, and the facts must
    assert on it.
  - `:240` asserts `Assert.Null(message.Headers)` with only `PropagateTestIdentity = false`. Under the **plan's own** R1
    (in-place, `PropagateTraceContext` default true) this green fact turns red, and the plan does not list it (READ).
    Under a copy it stays green but proves nothing unless it also checks the inner message.

**Change.** Adopt gRPC's rule. Hand the inner call a copy when Kronikol adds anything, never write the caller's
`Message`, and add each header only when the caller's message lacks it. T16 becomes "the caller's message is unchanged,
and each inner message carries exactly its own produce's identity and `traceparent`", with a sibling fact that keeps a
caller-set identity. List the five facts above as rewritten. The 5.3 mutation becomes "write into the caller's
message".

## H5. Kafka stamps the background identity; the consumer launders it into an attributed one — CONFIRMED (HTTP does the same)

**Evidence.**
- `InjectTestIdentityHeaders` (`TrackingKafkaProducer.cs:104-115`) stamps whatever `ResolveWithSource` returns. With
  `CaptureBackground` on, that includes the background identity (`TestInfoResolver.cs:119`, `:126-129`).
- gRPC sends only `IsAttributed` identities since 4.13.0 (`GrpcTrackingInterceptor.cs:568`).
- The HTTP handler at 4.14.3 still sends whatever it resolved (`TestTrackingMessageHandler.cs:214-230`, no
  `IsAttributed` check), and the receiving middleware opens a `Begin` scope with it
  (`TestTrackingContextMiddleware.cs:33`). The gRPC plan recorded both senders as "a separate question" (§8, Q3).
- So Kafka matches HTTP and differs from gRPC.

**What goes wrong on the consumer** (RUN P1, `CaptureBackground` on, nothing resolving): the produced message carries
`Unknown`/`unknown`. After `Consume`, the consumer flow resolves `Unknown/unknown source=Scope attributed=True`, where
it resolved `source=None attributed=False` before. `SetFromMessage` (`TestIdentityScope.cs:172-176`) makes it a scope,
and `ResolveWithSource` returns a scope as `Scope` (`TestInfoResolver.cs:113-114`). Consequences:
- the consume and everything the handler logs carry source `Scope`, not `None`;
- `AutoCorrelateOnConsume` stores `(Unknown, unknown)` for the key (RUN P1);
- gRPC's `IsAttributed` gate downstream of the consumer is defeated, so a gRPC call in the handler sends `unknown` as
  an attributed identity;
- the scope beats the consumer's `GlobalFallback` (non-detached) and its owner window (detached, `TestInfoResolver.cs:91-99`).

**Change.** The plan rewrites this method in R0 (call-time capture), so it should apply gRPC's rule there: stamp only
`identity.IsAttributed`. Add a guard fact (`CaptureBackground` on; nothing stamped; the consumer resolves its own
background). The receiving side's laundering (`SetFromMessage`/`Begin` accepting `UnknownTestId`) also affects HTTP.
Fixing it in core helps both; that belongs in section 8's sibling list.

## H6. Kafka never carries or takes the Kronikol trace id — CONFIRMED

**Evidence (READ).** `KafkaTracker.cs:112` and `:160` use `Guid.NewGuid()` for every entry. Every other path takes the
inbound `test-tracking-trace-id` of the request being served:
- HTTP: `TestTrackingMessageHandler.cs:199`, `:221`, `:227`;
- gRPC: `InboundTraceId`, `GrpcTrackingInterceptor.cs:565`, `:572-588`;
- `MessageTracker`: `MessageTracker.cs:362-372`.

The 4.13.0 changelog's stated reason is "so `kronikol query flow` nests the called host's calls". `flow` reads only
the Kronikol `traceId` (R4 clause (b), `CallNesting.cs:69-72`; FLOW_NESTING_PLAN F4 rejected `activityTraceId`), so
R1's W3C ids do nothing for `flow`.

**`flow` today and with the change** (RUN P8, the real internal `CallNesting.Parents` driven by reflection), for test →
host A (HTTP) → produce → consume in host B → B's HTTP call:

| Shape | Consume's parent | B's call's parent |
|---|---|---|
| S1 today, consume after the POST answered | top level | top level |
| S2 today, consume while the POST still waits | top level | top level |
| S3 = S2, message carries G1 (produce takes the inbound id, consume reads it) | POST /orders | top level |
| S4 = S3 + R2 (consume answered at next Consume) | POST /orders | Consume (B) |

With the change, a delivery nests under the call that published it while that call is open, which is clause (b)'s
purpose (FLOW_NESTING F3, the "564 deliveries"). B's own calls still need R2's window, or an ambient Kronikol trace id
set from the message: the HTTP handler takes it only from `HttpContext` (`:199`).

**Change.** Ask 2 should also:
- record the inbound Kronikol trace id on produce (as gRPC's `InboundTraceId` does);
- stamp it on the message as a header beside `kronikol-test-name`/`-id`, added only if absent, under
  `PropagateTestIdentity`;
- make the consume entry take it.

Add a `flow` fact for S3.

## H7. Ids alone do not put the handler's spans in the trace, and R1's ids can remove spans — CONFIRMED, plus a regression

**Do the handler's spans join the producer's trace without R2?** Only when the consumer parents its processing span
from `traceparent`.
- Confluent.Kafka.Extensions.Diagnostics does: `SetParentId` from the first `traceparent`, `ActivityDiagnosticsHelper.cs:85-105`.
- OpenTelemetry contrib does not. Its receive and process spans use `parentContext: default` and only link the message's
  context (`InstrumentedConsumer.cs:350-386`, `OpenTelemetryConsumeResultExtensions.cs:140-173`), so the handler's
  spans sit in the loop's trace or a new root.
- With no consumer instrumentation, nothing joins (READ).
- RUN P2 records both shapes: the linked handler's spans have `trace=other`.

**Overclaims.**
- C4: "ids alone fix that view".
- 4.5: "R1's ids put the handler's spans in the scenario's whole-test flow".
- 4.3 and the R1 changelog: "so a consume joins its producer's trace".

The entry joins. The handler's spans join only for a parenting consumer.

**Regression (RUN P2).** Test X: POST → host A produces (span = A's server span, as R1 records `Activity.Current`) →
consumer:

| | X's whole-test flow | X's POST popup |
|---|---|---|
| today, no Kafka ids | 4 spans (POST, db insert, parented process, handler db read) | 2 (POST, db insert) |
| R1, consume under X | 4 (R1 adds nothing: X's HTTP call already claims T) | 2 |
| R1, consume under Y (fetcher answering another test, RUN P1b) or `unknown` (background) | **1** | **1** |

- Whole-test flow: once two tests record T, `WholeTestSegmentsOf` drops the trace (`InternalFlowSegmentBuilder.cs:76`,
  `Count == 1`). X loses the handler's spans that it shows today.
- Popup: the consume's span id is A's server span, so in subtree mode (`:152-162`) its pool is A's whole request. Its
  50 ms tolerance (`:230-234`) reached A's `db insert` 39.6 to 46.2 ms earlier. Both tests took the span, so neither
  kept it (`:242-266`), and X's own POST popup lost it.
- The same tolerance puts the producer's `db insert` into the consume popup: 1 span with R1, 3 with R2's window.
- A consume lands under another test whenever the consumer's fetcher answers a different test (a host built inside a
  test, not detached; RUN P1b) or the background.

**Change.**
- Correct C4, 4.3, 4.5 and the changelog.
- Record a consume's ids only when the consume is attributed and only from the message's `traceparent` (see Other-1).
- Under R2, anchor on Kronikol's own process span, not the producer's span.
- Add a fact for the mis-attributed consume, and replace T31 (see Tests).

## H8. `traceparent` format, parsing, first or last — PARTLY

**Format.** It matches HTTP and gRPC: flags `01` when `Activity.Recorded`, random ids with `00` otherwise
(`TestTrackingMessageHandler.cs:171`, `:180-181`; `GrpcTrackingInterceptor.cs:529-535`, `:624`).
- In a test process where only Kronikol listens, `Recorded` is always false: the listener samples `AllData`
  (`InternalFlowActivityListener.cs:49-50`), so every injected header says `00`.
- No fact pins `01` for a recorded span, and a mutation to "always 00" survives 5.3.
- The plan's line references to these sites are stale since 4.12.1 and 4.14.3.

**Parsing (RUN P5).** Three parsers disagree:

| Input | .NET `ActivityContext.TryParse` | ProxyTap's public `TraceParent.TryParse` (`ProxyTapOptions.cs:183-195`) | The plan |
|---|---|---|---|
| upper case | rejects | accepts | lower-cases |
| version `ff` | rejects | accepts | rejects |
| all-zero span id | rejects | accepts | ambiguous ("not all zero") |
| trailing `-extra` | rejects | accepts | not said |
| version `01`, 55 chars | accepts | accepts | rejects |

The runtime requires lower-case hex (`Activity.cs:960-1003`), so an upper-case header Kronikol accepts is one that no
.NET consumer parents from. Recommendation: parse with `ActivityContext.TryParse` (net8.0+), drop T29's "lower-cased"
case, and log ProxyTap's leniency (version `ff`, zero span id) as a sibling defect. ProxyTap also forwards flags `01`
regardless of `Recorded` (`ProxyTap.cs:247`).

**First or last.**
- OpenTelemetry contrib appends with no Remove (`InstrumentedProducer.cs:382-386`) and reads the last (`TryGetLastBytes`,
  `OpenTelemetryConsumeResultExtensions.cs:200-206`).
- vhatsura appends with no Remove (`ActivityDiagnosticsHelper.cs:31-43`) and reads the **first**
  (`FirstOrDefault`, `:91-92`).
- So 4.6's "consumers read the last one" is false for vhatsura.

**Is T27 realistic?** Yes, on the package's main path. `AddKafkaProducerTestTracking` decorates the registered
`IProducer` (`KafkaServiceCollectionExtensions.cs:31-34`), so an instrumented producer sits inside Kronikol's wrapper.
(Build interception is the opposite: OTel's builder subclass and vhatsura's `BuildWithInstrumentation` wrap the
patched `Build()`, so they inject first and Kronikol leaves theirs.)

But in T27's shape the duplicate exists only because Kronikol injected first. vhatsura's consumer then parents on
Kronikol's header (the caller's span or a random id) instead of its own publish span. Kronikol would be changing the
trace structure it reports on.

**Change.** Inject only when `_inner` is Confluent's own client: nothing sits in between that could inject, the analog
of 4.14.3's `TransportPropagatesTraceContext()`. Otherwise read the inner wrapper's header after the call. Never add a
second `traceparent`. Reading the last one remains right for OTel contrib.

## H9. F8 timestamps and ordering — PARTLY (implementable; two constraints the plan misses)

**Evidence.**
- `RequestResponseLogger.Log` stamps only when `Timestamp` is null (`RequestResponseLogger.cs:57-60`; RUN P6: a preset
  timestamp is kept). `KafkaTracker` builds its own records, so no core overload is needed.
- `LogPair`'s one instant is `var now = UtcNow; requestAt: now, responseAt: now` (`:128-132`, internal overload
  `:138-175`).
- The Kafka internal overload must take `requestAt`, the captured identity and the captured phase.
  `PhaseConfiguration.ShouldTrack` and `GetEffectiveVerbosity` read `TestPhaseContext.Current` themselves
  (`PhaseConfiguration.cs:15-36`), so they must be computed at the call, not on the poll thread.

**The constraints.**
- **Enqueue both halves together at delivery, with the request's timestamp preset to the call.** Enqueuing the request
  at the call does not work for two reasons:
  - Raw verbosity's label and URI hold partition and offset, which exist only at delivery
    (`KafkaOperationClassifier.cs:12-15`, `:44-47`).
  - `flow` would nest any consume recorded in between under the open produce, by clause (a), since the produce's
    service is `Kafka`. RUN P8 S5: "Consume (B, other message) parent: Produce (A)".
- The diagram draws in store order (`PlantUmlCreator.cs:103-110`, no timestamp sort). A produce therefore still draws
  at delivery, and its timestamps disagree with that order; say so.

**Reading the traceparent after the inner call.** Implementable with a copy:
- capture `requestAt`;
- call the inner client;
- read the copy's header once the call returns. OTel contrib and vhatsura inject before their first await; for sync
  `Produce`, read before the handler runs;
- log both halves at delivery or failure.

**Change.** State all of this, and add a `flow` fact for S5.

## H11. Failure status — CONFIRMED

**Evidence.**
- `FailedSend.Status(ProduceException<string,string>)` is `!ProduceException`2` (RUN P0, `FailedSend.cs:16-20`).
- The diagram draws a "!" status verbatim (`PlantUmlCreator.cs:1155-1159`).
- `IsError(null, "!Local_MsgTimedOut")` is True (RUN P0, `InteractionStatus.cs:101-112`).
- The plan's `!{ErrorCode}` is therefore readable and classified as an error, but it is a second "!" convention.
- A delivery report has no exception, so `FailedSend` cannot be applied to it as is.

**Gaps.**
- **Cancellation.** Confluent registers `TrySetCanceled` and then enqueues anyway (`Producer.cs:800-818`). A cancelled
  `ProduceAsync` throws `TaskCanceledException` with the message still queued (RUN P4b: "still queued: 1"). Drawing it
  as a failed produce contradicts the consume that follows.
- **Sync `Produce` throwing.** `Local_QueueFull`, an unknown topic or serialization throws synchronously
  (`Producer.cs:886-944`). 4.2 covers only the delivery report and `ProduceAsync`, so this path still logs nothing.

**Change.** Specify:
- Status `"!" + Error.Code` for `KafkaException`/`ProduceException` and for delivery reports.
- `Error` = `FailedSend.Describe(ex)` when there is an exception, `report.Error.Reason` when there is not.
- `FailedSend.Status(ex)` for any other exception.
- A cancelled wait: an Error text saying delivery is unknown, plus a decision on whether it counts as an error.
- A fact each for a synchronous `Produce` throw and for cancellation.

## H12. R2's Activity left current after `Consume` — CONFIRMED

**Evidence.** `Activity.Stop()` sets `Current` to whatever was current at `Start`, unconditionally (`Activity.cs:743-762`).
RUN P3:

| Case | What happened |
|---|---|
| (a) direct call | works |
| (b) `await Task.Run(() => consumer.Consume(ct))`, a common BackgroundService shape | `Activity.Current` is null after; the handler's spans are not children |
| (c) loop wrapping each `Consume` in its own Activity | m2's parent is the stopped "iteration 1" activity |
| (d) two consumers in one loop | B's second span is parented by A's stopped first span |
| (e) a handler's leaked Activity | silently dropped from `Current`, never stopped |

**Further risks (INFERRED).**
- A `Commit` or `StoreOffset` called from another thread (a commit timer) stops the span on that thread and sets that
  thread's `Current`.
- A handler that throws out of the loop leaves the response until `Close`/`Dispose`. DI disposal at host shutdown then
  gives a `durationMs` that runs to shutdown.
- The deferred response must reuse the request's captured identity, phase, verbosity and ids. F10 clears the message
  identity at that same next `Consume`, and today `LogIncoming` re-resolves per call (`KafkaTracker.cs:151`).
- It must be logged at the **start** of the next `Consume`, before it blocks.
- The R2 span is `AllData` and not recorded, so a ParentBased sampler in the app drops the handler's child spans from
  the app's exports unless the app adds `Kronikol.Kafka` as a source.

**Change.**
- Make an explicit scope the primary mechanism (Q7, and #160's `TrackConsume` returning a disposable).
- If the implicit mode stays, stop the span without disturbing `Current` when it is not the span (save `Current`,
  `Stop()`, restore).
- Document `Task.Run`.
- Add facts for (b) to (d), the captured identity and the logging instant.

## H14. #135 ask 3, #160, #167 and Q8 — CONFIRMED (conflicting and redundant surface)

**What each asks.**
- #167 (filed 2026-10-11, after the plan): the value as the body; the key and the message's own headers as the entry's
  headers; `LogProduce`/`LogConsume` overloads taking the key and headers.
- #160: public `TrackProduce(topic, partition, message)`, which stamps and logs, and `TrackConsume(result)`, which logs
  and returns the identity scope; plus per-host interception.
- The plan: `ContentFormat = Json` with a `{"key":…,"value":…}` envelope (4.4), Q8 `Headers` overloads, and R2's
  `BeginConsume`.

**Conflicts.**
- The envelope moves #167's own query from `$.Status` to `$.value.Status`.
- It puts per-run keys (often GUIDs) into every body that `diff --body` compares (INFERRED).
- It keeps the key inside the body, while #167 wants it shown above the body regardless of the header toggle.

**Redundant surface if all ship.**
- `KafkaContentFormat` and `ContentFormat`. Shipping them with default `Text` would also make a later move to
  "value as body" a change of a public option's default, which CLAUDE.md classes as major.
- Q8's overloads, #167's overloads, #160's helpers and `BeginConsume` all overlap.

**How today's readers would serve #167's shape (READ).**
- `RequestResponseLog.Headers` is `(string, string?)[]`.
- `query http --headers` prints any entry's headers (`QueryCommand.Payloads.cs:339-347`).
- The note draws headers as gray lines, and the YAML view skips them (`collapsible-notes-script.js:1021-1031`).
- `values`, `--where` and `diff --body` evaluate the body as JSON (`QueryCommand.Values.cs:48-49` defaults to the
  response, so Kafka still needs `--request`; Q11 stands).
- Kafka header bytes become text with a strict UTF-8 decode and base64 otherwise. A null value stays null.
- Read the caller's headers before Kronikol adds its own, as gRPC logs the caller's metadata, which answers Q9's
  objection.
- On the consume side, filter `kronikol-test-*`.

**One design.**
- No content enum: the body is the value, using 4.4's per-type rules with `TrackingSafeSerializer` for "anything else".
- The key goes in a reserved header, the message's headers in the entry's headers.
- `KafkaTracker` exposes `TrackProduce<K,V>` (returns the message to send, a copy with identity, trace id and
  `traceparent`, plus `Delivered`/`Failed` completion) and `TrackConsume<K,V>` (logs, sets the message identity and
  R2's span, returns `IDisposable`).
- `TrackingKafkaProducer`/`TrackingKafkaConsumer` are built on these.
- This gives the reporter's hand-written decorators (the motivating user of #135, #160 and #167) R0's fixes, which
  4.2 confines to an internal overload.

---

## Other defects, by severity

1. **High. The consume falls back to the loop's `Activity.Current`** (4.3 row 2, 4.6). A hosted loop's Activity is
   ambient: inherited, long-lived or stale (`DetachedHostedService` detaches identity, not `Activity.Current`,
   `DetachedHostedService.cs:42-46`). Every consume of every test then claims one trace, and P2's mechanism strips
   spans. Record a consume's ids only from the message's `traceparent` (or R2's span); keep `Activity.Current` for
   hand-written logs.
2. **High. Mis-attributed consume plus ids** strips the producing scenario's popup and whole-test flow (H7, RUN P2).
3. **Medium. Kronikol's injection breaks vhatsura's consumer parenting** on the DI path (H8).
4. **Medium. Failures the plan does not cover:** synchronous `Produce` throws; cancellation (H11). A `ConsumeException`
   carries the record and its headers (`ConsumeException.cs:42-68`; `Consumer.cs:883`, `:916`, `:950`), yet a failed
   consume is never drawn and the plan has no design for it.
5. **Medium. Background identity laundering** (H5); gRPC's rule ignored.
6. **Medium. R2 lifetime** (H12) and the deferred response's identity.
7. **Medium. Kronikol trace id missing on messages** (H6).
8. **Low.** Three `traceparent` parsers, plus ProxyTap's lenient public parser (H8).
9. **Low.** Section 2's "On every entry" conflicts with 4.3, which covers produce and consume only (subscribe, commit,
   flush and transactions get no ids). The R1 changelog omits 4.3's `Activity.Current` fallback for consume.
10. **Low.** 4.4: "date → the JSON primitive" (JSON has no date); `{"base64":…}` cannot be told from a real value of
    that shape; `Ignore` is untested.
11. **Low.** Stale line references: HTTP `:156-169` is now `:162-183`; gRPC `:335-353` is now `:529-535` and
    `:623-624`.

## Tests (5.1 to 5.3)

**Facts that cannot fail, or pass for the wrong reason.**
- T31 passes trivially if the "handler" runs on the test's flow under the test's ambient Activity, which is the
  produce's trace. It needs a consumer on its own flow, a parenting consumer, and a sibling for the linking consumer,
  which must not join.
- T24's `ProduceAsync` half: the `ExecutionContext` restores `Activity.Current` after the await, so "read after the
  await" is indistinguishable.
- T35's "byte-identical, ids apart" cannot see the internal-flow changes in P2.
- T28 cannot catch "not lower-cased" (only T29 can).
- T16 and T27 pin harmful behaviour (H4, H8).

**Missing facts.**
- flags `01`;
- attributed-only stamping;
- the consume ambient fallback;
- the mis-attributed consume's effect on internal flow;
- both halves enqueued together (S5) and the `flow` nesting (S3, S4);
- synchronous `Produce` throw, cancellation, `ConsumeException`;
- phase captured at the call;
- R2 under `Task.Run`, a wrapping loop and two consumers;
- the deferred response's identity;
- the publish span;
- `BeginConsume`;
- `Kronikol.Kafka` as a well-known source.

**Mutations no listed fact catches.** "Flags always 00"; "send the background identity"; "consume records ambient
`Activity.Current`"; "enqueue the request at the call"; "deferred response re-resolves identity"; "write into the
caller's message".
