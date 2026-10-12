# Kafka interception and trace plan: #135

**Written:** 2026-10-07, at 4.9.0 (`417c8e58`), the day #135 was filed; moved onto 4.11.0 (`709b3afc`) on
2026-10-08; **audited on 2026-10-12 against 4.14.6 (`d59843fa`)**, and checked the same day against 4.14.9
(`5b2c3a32`), where of the files it cites only `ROADMAP.md` had moved. The audit re-checked every citation and claim,
re-ran the probes, and corrected the design itself: build interception keyed by code shape and constructing without
wrapped exceptions (4.1), the caller's message never written (4.2), failures drawn on every path, consume ids recorded
only where they cannot strip another test's spans (4.3), ask 3 reconciled with #167 and the hand-written path with
#160 (4.4, Q16, Q17), and R2's lifetime (4.5). Section 11's last entry lists every change. **Status: green-lit in
full by the owner on 2026-10-12, every question in section 9 as recommended (D37); executing.** The probes are in the
harness. Evidence labels: **RUN** (measured here; the probe, its
source and what it printed are in the harness), **READ** (in the source at `d59843fa`, `file:line`, unless another
revision is named, `ROADMAP.md`'s lines being those at `5b2c3a32`, or in Confluent.Kafka 2.15.1's source), **INFERRED** (reasoned from facts, stated by none), **ISSUE**
(taken from #135, #160 or #167, not re-measured), **DOC** (a third party's documentation). The harness is
[`KAFKA_INTERCEPTION_AND_TRACE_PLAN.harness/`](KAFKA_INTERCEPTION_AND_TRACE_PLAN.harness/README.md); the audit's
notes are in its `audit/` folder.

#135 makes three asks of the Kafka extension, found while integrating 4.6.0 into a .NET 8 suite of ten in-process
hosts: (1) intercept `DependentProducerBuilder<,>.Build()` as well as `ProducerBuilder<,>.Build()` and
`ConsumerBuilder<,>.Build()`; (2) record W3C trace ids on produce and consume entries, so `kronikol query trace` follows
a call into Kafka and out of the consumer, and a consume arrow's internal-flow popup finds the handler's spans;
(3) optionally log the message as a JSON envelope so `kronikol query values --path` can reach into it. The same
reporter has since filed #160 (2026-10-08: a library that builds its clients behind its own interfaces cannot be
tracked without copying the wrappers' private code, and interception cannot tell hosts apart) and #167 (2026-10-11:
the content line is not indented, has no YAML view and cannot be queried; record the value as the body). Both touch
the code and the public surface this plan changes (section 8, Q16, Q17).

All three asks reproduce on 4.9.0 and still on 4.14.6 (section 1). Two need more than the issue asks:

- **Ask 1 cannot be built on today's mechanism.** The shipped interception patches Confluent's *shared* generic code.
  Enabling `<string,string>` reroutes every other reference-type pair: `new ProducerBuilder<Null,string>(...).Build()`
  returns a `TrackingKafkaProducer<String,String>`, and its first `Produce` throws `EntryPointNotFoundException`.
  `Reset()` does not repair it (F1, RUN). Separately, tiered compilation re-JITs the patched method, and interception
  stops without a word after as few as 33 to 40 builds of a shape once the process has quiet periods, as a test run
  does (47 to 251 in a busy loop) (F2, RUN). Both are live in every release since 2.27.16 and reproduce on 4.14.6 with
  Lib.Harmony 2.4.2 and 2.4.3, so they ship first, alone, as a patch (R0). Ask 1 is then one more patch site on the
  corrected mechanism (R1).
- **Trace ids alone do not fix the popup, and on a consume they can do harm.** The popup takes spans that start
  between a call's request and response (`InternalFlowSegmentBuilder.cs:182-236`), and a consume entry is logged at
  one instant, before the handler runs. A probe gave the right trace and span id with today's timing 0 spans, and the
  same entry with its response stamped after processing both spans (C4, RUN). The handler's spans are in the message's
  trace only when the consumer parents its processing span on the message's `traceparent`, which one instrumentation
  package does and OpenTelemetry's contrib package does not (C4). And a consume recorded under another test than the
  producing one, as a consumer whose framework fetcher answers another test is, makes two tests claim the producer's
  trace: the whole-test flow then drops that trace and the popup's shared spans go to neither test, so the producing
  test loses spans it shows today (section 4.3, RUN). R1 records a consume's ids only from its message and only for the test
  the message names; the arrow popup needs the consume to end when the message is done, which is section 4.5 and a
  separate, opt-in release (R2) pending Q6.

The research also found that a sync `Produce(..., handler)` is attributed to the test that *built* the producer, that
a failed produce is never drawn or is drawn as a success, that a tracked producer with delivery reports off throws on
a plain `Produce`, and smaller defects in timing, headers, labels and docs (section 3). They sit on the lines R1 edits
and ship in R0. The audit added a wider reach for F1 (pairs that share a value-type argument, F18), a tracker
registered again on every `Enable…` (F19), a produced message that carries the background identity (F20, Q18), the
Kronikol trace id that HTTP, gRPC and `MessageTracker` carry from hop to hop and Kafka does not (F21, Q19), failures
still drawn nowhere (a synchronous `Produce` that throws and a failed consume, F22), hosts that enable at once losing
their first build (F23), a dependent producer whose owner reports no deliveries logging nothing (F24), and the wrapper
writing into the caller's `Message` (F9, now fixed by a copy).

## 0. Summary

| | |
|---|---|
| What ships | **R0, a patch:** build interception that returns the right client for every pair, keyed by code shape, never unpatches and throws what `Build()` throws (F1, F18), that says when tiered compilation can end it and how to stop that (F2); the producer, consumer and doc defects F3 to F13, F15, F16 and F19 to F24, among them hosts that enable at once losing their first build, the caller's `Message` never written and every failed produce or consume drawn; and the coverage F17 lists. **R1, a minor:** `DependentProducerBuilder` interception (ask 1); W3C ids on produce and consume entries where they cannot strip another test's spans, and a `traceparent` on a produced message that has none when Kronikol's wrapper is the last before Confluent (ask 2); the message content in the shape Q16 settles, recommended #167's (ask 3); the hand-written path Q17 settles, recommended #160's helpers in place of Q8's overloads. **R2, a minor, only if Q6 says so:** an opt-in processing trace that makes the consume arrow's popup show the handler's spans |
| What it records | R0: nothing new; failed produces, deliveries and consumes are drawn, a sync produce is its caller's, and durations are measured or absent (never invented). R1: `activityTraceId`/`activitySpanId` on produce and consume entries; with Q16 as recommended, the value as the body and the key and the message's headers as the entry's headers |
| What it cannot see | Interception after tier-up in a test process that keeps quick JIT on (the wiki gives the one-line remedy); the processing span of a consume before R2; the handler's spans in the message's trace when the consumer links rather than parents; ids on a consume recorded under another test than its message names; which host built an intercepted client (#160); a trace across a message no one stamped with `traceparent` (section 4.6) |
| Bumps | R0 patch, R1 minor, R2 minor (6.2). New surface: `PropagateTraceContext` (R1, Q5), `KafkaTracker.TrackProduce`/`TrackConsume` (R1, Q17), a Kronikol trace id header (R1, Q19), and R2's switch. No content option if Q16 is answered as recommended |
| Waits for | Nothing: Q16 and Q17, which decide R1's public surface (#167, #160), and Q5 and Q19 were taken as recommended. A real broker is needed only for R1's end-to-end facts (Testcontainers in CI, section 5.4) |
| Decided | Q1 to Q19 (section 9), all as recommended (D37, 2026-10-12). Q1 (the tier-up remedy), Q5 (does `traceparent` propagate by default), Q6 (the popup), Q16 (content shape), Q17 (the hand-written path) and Q18 (the background identity) shape the work |

## 1. How far each claim was checked

| # | The issue says | Level | Verdict |
|---|---|---|---|
| C1 | `KafkaBuildInterceptor` patches only `ProducerBuilder<,>.Build()` and `ConsumerBuilder<,>.Build()`; `DependentProducerBuilder<,>.Build()` is not intercepted | RUN, READ | **Reproduced.** With `<string,string>` enabled, `new DependentProducerBuilder<string,string>(owner.Handle).Build()` returns Confluent's own `Producer<String,String>` (`s0/shipped/results.txt`, both runtimes). Patch sites `KafkaBuildInterceptor.cs:123-143` |
| C2 | Produce and consume entries carry no `ActivityTraceId`/`ActivitySpanId` | RUN, READ | **Reproduced.** Under an ambient `new Activity("probe").Start()`, `KafkaTracker.LogProduce` writes two entries with `ActivityTraceId` null (`s0/shipped/results.txt`). `KafkaTracker.cs:112-146` sets neither field; the pair's `TraceId` is Kronikol's own `Guid`, not W3C |
| C3 | So `kronikol query trace` cannot follow a call into Kafka and out of the consumer | READ | **Holds.** `trace` keeps the calls whose request carries the id (`QueryCommand.Trace.cs:97-99`); an address with no id is refused (`:46-50`) |
| C4 | The consume arrow's popup finds only the consume span, because the entry is logged at one instant | RUN, READ | **Holds, and ids alone do not fix it.** The window is the request's time less 50 ms to the response's (`InternalFlowSegmentBuilder.cs:182-236`). A consume pair with the right trace and span id and today's timing took 0 of the handler's spans; the same pair with the response stamped after processing took both (`research-kronikol.md` §1). The scenario's whole-test flow is not time-filtered, but it takes a trace's spans only when one test claims the trace (`:74-78`), and the handler's spans are in the message's trace only when the consumer parents its processing span on the message's `traceparent`: `Confluent.Kafka.Extensions.Diagnostics` does (`ActivityDiagnosticsHelper.cs:85-105`), OpenTelemetry's contrib package starts its spans with `parentContext: default` and only links the message's context (`InstrumentedConsumer.cs:383-385`, `OpenTelemetryConsumeResultExtensions.cs:171-173`), and with no instrumentation nothing joins (audit `C-review.md` H7, RUN). So ids fix that view for a parenting consumer, and R2 for the rest. In the reporter's setup, where the test's own HTTP call already claims the trace, R1's consume ids add nothing to it (RUN) |
| C5 | Recording `Activity.Current`'s ids when the entry is logged (the producer's span; the consumer's processing span) would connect them | RUN, READ | **Half right.** For `ProduceAsync`, `Activity.Current` after the await is the caller's again (Confluent completes the task with `RunContinuationsAsynchronously`); for `Produce(..., handler)` the handler runs on the owner's poll thread with the context of whoever *built* the producer (`research-confluent.md` §4.2, RUN), so the ids must be taken at the call. For a consume, no processing span exists yet when `Consume()` returns: neither OpenTelemetry's contrib package nor `Confluent.Kafka.Extensions.Diagnostics` has started one (§7.4, READ). The message's `traceparent` header is the only id there; `Activity.Current` at a consume is the polling loop's, inherited from whoever started the host, long-lived or stale (`DetachedHostedService` detaches the identity, not the `Activity`, `DetachedHostedService.cs:42-46`), and recording it would make every test's consume claim one trace (4.3). For a hand-written `LogConsume` inside the caller's own processing span (the reporter's setup), `Activity.Current` is right |
| C6 | The content is logged as `Key: k, Value: v` text, so `query values --path` cannot reach into a JSON or protobuf value | RUN, READ | **Holds.** Content `Key: k, Value: {"a":1}` (`s0/shipped/results.txt`); `values` evaluates JSON only and a name step matches only inside a JSON object (`PathEngine.cs:169`), so the value must be embedded as JSON, not as a JSON string. `values` reads response bodies unless told otherwise (`QueryCommand.Values.cs:48-49`) and Kafka's content is on the request half, so the recipe needs `--request` (`Querying-Reports.md:617-648`, `:636`). #167 asks for the value itself as the body rather than an envelope around it (Q16) |

## 2. Where it stands today

| The code holds | Today (4.9.0 to 4.14.6) | After R0 | After R1 |
|---|---|---|---|
| Build interception, other pairs of the enabled pair's shape | Wrong client type, throws on first use (F1, F18) | Correct type, tracked if enabled | Same, `DependentProducerBuilder` too |
| Build interception after a few dozen builds (about 40 with pauses) | Silently stops (F2) | Stops unless quick JIT is off; the run says so | Same |
| Hosts that enable at once | 9 of 10 first builds untracked (F23) | Every first build tracked | Same |
| `Reset()` | Unpatches, which cannot restore the shared code (F3) | Clears tracking, leaves the patch | Same |
| Sync `Produce(..., handler)` | The builder's test, the builder's `Activity`; a failed delivery drawn as a success (F4, F5) | The caller's test; failure drawn | Plus the caller's ids |
| Failed `ProduceAsync`, a `Produce` that throws, a failed consume | Nothing logged (F6, F22) | Drawn with its error | Plus ids |
| Delivery reports off | Plain `Produce` throws `InvalidOperationException` (F7) | Works | Same |
| Durations | Each half stamped separately: 0.0002 to 0.23 ms shown as "0 ms" (F8) | Send to delivery for a produce; none for one-instant entries | Same |
| The caller's `Message` | Gains a pair of identity headers per produce (F9) | Never written: Confluent gets a copy | Same, `traceparent` included |
| The background identity | Stamped on messages; a consumer adopts it as a scenario's (F20) | Per Q18 (recommended: not sent, as gRPC) | Same |
| Kronikol trace id | A new one per Kafka entry; nothing on the message (F21) | Same | Per Q19 (recommended: taken from the request served and carried on the message) |
| W3C ids | None (C2) | None | On produce and consume entries (a consume's only from its message, for the test the message names); `traceparent` added when Kronikol's wrapper is the last before Confluent |
| Content | `Key: k, Value: v` | Same | Per Q16 (recommended: the value as the body, the key and the message's headers as the entry's headers) |

## 3. Findings the issue does not state

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **Interception corrupts every other reference-type pair.** Harmony compiles the replacement from the *closed* `<string,string>` IL, so `newobj Producer<string,string>` lands in the code every reference-type instantiation shares. With `<string,string>` enabled, `ProducerBuilder<Null,string>`, `<string,byte[]>` and `ConsumerBuilder<Null,string>` return clients of the wrong type and the first call throws `EntryPointNotFoundException`. A no-op postfix (what `Disable…` leaves) does the same; with two pairs enabled the last one wins for all; `Reset()`/`UnpatchAll` never restores the other instantiations. Pairs of another code shape have their own code and are untouched, but a value-type argument does not give a pair code of its own (F18). Live since 2.27.16: the closed postfix is in the package's first commit (`004589fe`) | RUN | `s0/shipped`, `s0/kronikol` (`pairs`), `s0/harmony/*`; `KafkaBuildInterceptor.cs:123-153` |
| F2 | **Tier-up silently ends interception.** The shipped package stopped wrapping after 151 `<string,string>` builds on .NET 8 at 4.9.0 (`s0/kronikol` `tierup`; 116 to 151 across runs and runtimes, and 201 to 601 calls in the generic probes); on 4.14.6, after 47 to 251 in a busy loop, and after 40 in every run with a 200 ms pause every 10 builds, as between tests, where the first draft's prototype stopped at 33 to 40 (audit `B/`, X2: the runtime's call-count threshold of 30 and its 100 ms delay, INFERRED). The same with Lib.Harmony 2.4.3, published 2026-10-11 (RUN). A control shows Harmony 2.4.2 re-applies its detour after tier-up for non-shared methods and not for shared generic code. `PrepareMethod` and warming before patching do not help; re-patching after a bypass does, but needs the bypass to be seen, and tiered PGO makes two transitions (an enable-time warm-up passed its check and was bypassed later in 2 of 3 .NET 8 runs). `DOTNET_TC_QuickJit=0`, `DOTNET_TieredCompilation=0` and the MSBuild property `<TieredCompilationQuickJit>false</TieredCompilationQuickJit>` each held 20,000 of 20,000 | RUN | `s0/kronikol/out-tierup-net8.txt`, `tiermit/results.txt`; upstream Harmony #307, #526 (DOC) |
| F3 | `Reset()`'s doc says it "removes all Build() patches" and `Disable…`'s that "the Harmony patch remains but becomes a no-op"; the wiki says pairs are independent and `Reset()` cleans up between tests. With F1, none of these holds | READ | `KafkaBuildInterceptor.cs:102`, `:108`, `:114`; wiki `Integration-Kafka-Extension.md:452`, `:505-516`, `:528` |
| F4 | **A sync produce is the builder's, not the caller's.** `Produce(..., handler)` logs inside the wrapped delivery handler, which Confluent runs on the owner's poll task; that task captured the `ExecutionContext` of whoever called `Build()`. Built under test A and called under test B, both entries are A's, while the message's identity headers, stamped at the call, name B. Dependent producers deliver on the owner's thread too. Two doc comments #136 added in 4.11.0 say the opposite ("a produced message carries the scenario the produce is recorded under") | RUN | `s0/kronikol/out-attribution-net8.txt`; `TrackingKafkaProducer.cs:54-90`, comment `:108`; `KafkaTrackingOptions.cs:70`; Confluent `Producer.cs` poll task (`research-confluent.md` §4.1-4.3) |
| F5 | A failed delivery is drawn as a successful produce: the wrapped handler never reads `report.Error` (the probe's `Produce` had no broker, timed out, and was logged as an ordinary produce) | RUN, READ | `TrackingKafkaProducer.cs:62-68`, `:81-87`; `out-attribution-net8.txt` |
| F6 | A failed `ProduceAsync` logs nothing: the exception leaves before `LogProduce`. 4.4.1 fixed the same class for SQL; the HTTP handler draws a thrown send with its error | RUN | `TrackingKafkaProducer.cs:24-37`, `:39-52`; `out-attribution-net8.txt` |
| F7 | With `EnableDeliveryReports = false`, a tracked producer's plain `Produce(topic, message)` throws `InvalidOperationException: A delivery handler was specified, but delivery reports are disabled.` The wrapper always passes a handler; the same call on Confluent's producer succeeds | RUN | `out-dr-disabled-net8.txt`; `TrackingKafkaProducer.cs:62`, `:70`; Confluent `Producer.cs:874-877` |
| F8 | Each half of a Kafka pair is stamped by `RequestResponseLogger.Log` as it is enqueued, so every Kafka call gets an invented duration of 0.0002 to 0.23 ms ("0 ms"). 4.7.3 fixed this for `LogPair` (one instant, no `durationMs`) | RUN, READ | `KafkaTracker.cs:115-146`; `RequestResponseLogger.cs:59-60`, `:128-132`; `research-kronikol.md` adjacent defect 1 |
| F9 | The identity headers are added to the caller's `Message` (`Headers.Add` appends), so a message produced twice carries two pairs, one more per produce | READ | `TrackingKafkaProducer.cs:104-115` |
| F10 | A message's identity outlives it: `Consume` clears the previous message's identity only when a message arrives, so an empty poll or a partition EOF leaves the last message's test on everything the loop does next | READ | `TrackingKafkaConsumer.cs:70-81` (null and EOF return at `:72` before the clear at `:87`) |
| F11 | The Raw label of an operation without a topic ends with a space (`"Commit "`); the wiki shows `Commit` | READ | `KafkaOperationClassifier.cs:12-15`; wiki `Integration-Kafka-Extension.md:41-48` |
| F12 | `KafkaOperationClassifier`'s summary says it "Classifies Apache Kafka HTTP requests … based on URL patterns and HTTP methods" | READ | `KafkaOperationClassifier.cs:3-5` |
| F13 | A phase override cannot show content when the base verbosity is `Summarised`: both wrappers' `BuildContent` return null on `_options.Verbosity == Summarised` before the tracker picks each phase's variant, so `ActionVerbosity = Detailed` draws no content | READ | `TrackingKafkaProducer.cs:94`, `TrackingKafkaConsumer.cs:122`; `KafkaTracker.cs:106-110`, `:125-130` |
| F14 | The Kafka extension cannot start the internal-flow listener: `InternalFlowActivityListener.EnsureStarted` is internal, and `Kronikol.csproj`'s `InternalsVisibleTo` names gRPC, ClickHouse.Driver, ProxyTap and Playwright (the last two since 4.12.1), not Kafka. A Kafka-only suite captures no spans for ids to join. The only callers are the HTTP handler and the gRPC interceptor | READ | `InternalFlowActivityListener.cs:66`; `Kronikol.csproj:13-26`; callers `TestTrackingMessageHandler.cs:138`, `GrpcTrackingInterceptor.cs:525` |
| F15 | Wiki: the manual DI example hands the producer `new KafkaTrackingOptions()` instead of the tracker's options; `API-Reference.md`'s Kafka section lacks seven options (one of them #136's `HttpContextAccessor`, never added), the five transaction members of `KafkaOperation` and the two factory registrations, and says the registrations "auto-resolve `IHttpContextAccessor` from DI", which the option overrides since 4.11.0; the Kafka page still says `CurrentStepTypeFetcher` returns the step type, which its source has called a no-op since 4.12.1 | READ | `Integration-Kafka-Extension.md:179`, `:195`; `API-Reference.md:245-258`; `KafkaTrackingOptions.cs:20-25` |
| F16 | Wiki: "the `activityTraceId` every interaction carries since 3.0.47" and "end up on every interaction"; the `query trace` source says the same ("exported on every interaction since 3.0.47"). False for Kafka, and for every tracker except HTTP, gRPC, `MessageTracker`, OTLP, ProxyTap, TcpTap and the DispatchProxy/MediatR proxy in deferred log mode with an `ActivitySourceName` | READ | `Querying-Reports.md:842-862`; `Integration-OpenTelemetry-Extension.md:7-8`; `QueryCommand.Trace.cs:6`; writers listed in `research-kronikol.md` §5, plus `TrackingProxy.cs:101-108`, `:203-210` |
| F17 | Coverage: the sync `Produce` path, `ProduceAsync(TopicPartition, …)`, `Consume(int)`/`Consume(CancellationToken)`, EOF and null results, `Subscribe(IEnumerable<string>)`, `AutoCorrelateOnConsume`/`ConsumeKeyExtractor`, phase verbosity and content through the producer are untested; the build-interception tests use only `<string,string>` and `<int,string>`, net10.0 only | READ | `research-tests-docs.md` §1.4 (G1 to G10); `KafkaBuildInterceptorTests.cs:162-174`; `TrackingKafkaProducerTests.cs:332-333` |
| F18 | **F1 reaches pairs that share a value-type argument.** The runtime shares one body among instantiations that agree on their value-type arguments and are otherwise reference types: `<int,string>` and `<int,byte[]>` share `<int,__Canon>`. So on 4.14.6, with `<int,string>` enabled, `ProducerBuilder<int,byte[]>`, `<int,Null>` and `ConsumerBuilder<int,byte[]>` return the `<Int32,String>` wrapper and throw `EntryPointNotFoundException` on first use, and with `<string,int>` enabled so do `<Null,int>` and `<byte[],int>`; generic value types share by their own arguments, so `KeyValuePair<Uri,Uri>` keys share `KeyValuePair<string,string>`'s code. The plan's first draft said a value-type pair "has its own code" (4.1, T6) | RUN | audit `B/B-results.md` E2 (net8.0, net10.0, Harmony 2.4.2 and 2.4.3); `KafkaBuildInterceptor.cs:134-143` |
| F19 | **Every `Enable…` registers another tracker.** `EnableProducerTracking`/`EnableConsumerTracking` construct a new `KafkaTracker`, whose constructor registers it for good (`KafkaTracker.cs:20`, `TrackingComponentRegistry.cs`); `EnableTracking` makes two. A suite that enables per test, or enables both directions for an app that only produces, ends each run with "Warning: N tracking component(s) were registered but never invoked: KafkaTracker (Kafka), …" on the console (`ReportDiagnostics.cs:101-108`, `ReportGenerator.cs:639-645`) for trackers it never meant to leave unused, every one named "KafkaTracker (Kafka)", so a replaced producer tracker cannot be told from an idle consumer tracker, under a message that blames the wrong pipeline. Measured: enabling a pair twice leaves 2 registered and 1 unused after a produce; `EnableTracking` in an app that only produces, 1 unused; ten in-process hosts each enabling the same pair, 9 unused, and every host's entries carry the last host's `CallerName` (4.6) | RUN | audit `B/B-results.md` E8; `KafkaTrackingInterceptor.cs:30-73` |
| F20 | **The background identity travels as a scenario's.** With `CaptureBackground` on, `InjectTestIdentityHeaders` stamps `Unknown`/`unknown` on a message produced outside any scenario; the consumer adopts it (`TestIdentityScope.SetFromMessage`), so the consume and the handler's calls resolve with source `Scope`, attributed, where they resolved `None`; `AutoCorrelateOnConsume` stores `(Unknown, unknown)` for the key; and gRPC's `IsAttributed` gate downstream (`GrpcTrackingInterceptor.cs:568`) passes it on. gRPC stopped sending it in 4.13.0; the HTTP handler and the other messaging producers still do, which is the gRPC plan's open Q3 (`ROADMAP.md:955`) | RUN | audit `C/probe-output.txt` P1; `TrackingKafkaProducer.cs:104-115`; `TestIdentityScope.cs:172-176`; `TestInfoResolver.cs:113-114` |
| F21 | **Kafka carries no Kronikol trace id.** Every Kafka entry gets a new `TraceId` (`KafkaTracker.cs:112`, `:160`), where the HTTP handler, the gRPC interceptor and `MessageTracker` take the trace id of the request being served (`TestTrackingMessageHandler.cs:199`, `:221`, `:227`; `GrpcTrackingInterceptor.cs:565`, `:571-588`; `MessageTracker.cs:362-372`), and nothing carries one on the message. `kronikol query flow` nests a delivery under the call that published it only by that id (`CallNesting.cs:69-72`; W3C ids are not read there), so a consume never nests under the open call whose handler produced it | RUN | audit `C/probe-output.txt` P8 (the real `CallNesting.Parents`) |
| F22 | **Two failures are still drawn nowhere.** A synchronous `Produce` that throws (`Local_QueueFull`, an unknown topic, a serializer's error, Confluent `Producer.cs:886-944`) leaves before anything is logged, and a `Consume` that throws `ConsumeException` (a deserializer's error, a broker error) is never drawn, though the exception carries the record, its topic, partition, offset and headers (`ConsumeException.cs:42-68`; `Consumer.cs:883`, `:916`, `:950`) | READ | `TrackingKafkaProducer.cs:54-90`; `TrackingKafkaConsumer.cs:24-43` |
| F23 | **Hosts that enable at once lose their first build.** `PatchProducerBuild` marks the type patched (`TryAdd`) before it patches, so a host that loses the race returns at once and builds against the unpatched method: with ten hosts enabling together, 9 of 10 first builds were untracked on 4.14.6, every run, both runtimes; a host that builds its producer once at startup stays untracked for the run. `Harmony.Patch` is not idempotent either (each call appends a prefix) | RUN | audit `B/B-results.md` E3; `KafkaBuildInterceptor.cs:123-143` |
| F24 | **A dependent producer on an owner with delivery reports off logs nothing.** The dependent producer's own `enableDeliveryReports` defaults to true, so `Produce(…, handler)` is accepted, but the owner registered no delivery callback, so the wrapped handler never runs: a tracked dependent producer (by `.Tracked()` or DI decoration today, by interception in R1) records no produce at all. Confluent also leaks the handle it allocated for each such handler (READ) | RUN | audit `B/B-results.md` E6 (iii); Confluent `Producer.cs` |

## 4. The design

### 4.1 Build interception (R0 for F1 to F3, R1 for ask 1)

**One non-generic dispatching prefix per builder kind** replaces the closed postfixes (the harness's `prototype/` is
the executable spec):

```csharp
// Installed on ProducerBuilder<,>.Build (R0) and DependentProducerBuilder<,>.Build (R1); a sibling for ConsumerBuilder.
static bool ProducerBuildPrefix(object __instance, ref object __result)
{
    var (key, value) = BuilderArguments(__instance.GetType());   // walks base types, so a subclass's base.Build() works
    __result = ProducerShapes.For(key, value).BuildAndWrap(__instance);  // cached: new Producer<K,V>(builder) through
                                                                    // the internal ctor, then KafkaTrackingInterceptor.WrapProducer<K,V>
    return false;  // the original body was compiled from one closed pair's IL (F1): it must never run
}
```

- **Patch once per code shape.** The runtime shares one body among every instantiation whose type arguments agree
  position by position on value types and are otherwise reference types: `<string,string>`, `<Null,string>` and
  `<string,byte[]>` share `<__Canon,__Canon>`, and `<int,string>` and `<int,byte[]>` share `<int,__Canon>` (F18). A
  patch is therefore keyed by that shape, computed recursively (each argument is its own value type, with a generic
  value type's own arguments reduced the same way, or "reference": `KeyValuePair<Uri,Uri>` shares
  `KeyValuePair<string,string>`'s code), and installed through the shape's representative, the instantiation with
  `object` for every reference type. Measured (audit `B/` E2c): one patch per shape, and `<string,string>`,
  `<Null,string>`, `<int,byte[]>`, `<int,string>` and `<KeyValuePair<Uri,Uri>,string>` each got its own correctly typed
  client, tracked exactly when enabled. Every `Enable…<K,V>` ensures its own shape is patched: an enabled pair whose
  shape no patch covers is silently untracked (`<long,string>` beside a patched `<int,string>`, E2b). Which pairs are
  *tracked* is still `KafkaTrackingInterceptor`'s per-pair dictionary (`KafkaTrackingInterceptor.cs:95-120`); an
  untracked pair of a patched shape gets exactly the client Confluent's `Build()` makes. The pair comes from the
  builder's type or the nearest generic base type: the prototype's `__instance.GetType().GetGenericArguments()` threw
  `ArgumentException` for a non-generic subclass, enabled or not (E2b; T7 covers one).
- **Construct without wrapping exceptions, through a compiled delegate.** The prototype calls the internal constructor
  through `Activator.CreateInstance` and the wrapper through `MethodInfo.Invoke`, so every builder Confluent refuses
  throws `TargetInvocationException` from a patched shape's `Build()`: of five refusals measured, unpatched `Build()`
  threw `InvalidOperationException` (an unknown property), `ArgumentException` (two), `FormatException` and
  `ArgumentNullException` (a key type with no serializer), and the prototype wrapped all five (RUN, audit `B/` E1). A
  caller catching those at startup would stop catching them. The shape cache holds a delegate compiled once per pair
  (`Expression.New` on the constructor, then a direct call of `WrapProducer<K,V>`), which throws exactly what
  `Build()` throws (T37); `BindingFlags.DoNotWrapExceptions` does too, but reflection on every build also shortened the
  tier-up window, to 33 to 40 builds against 60 to 201 for the compiled delegate in a busy loop (X2).
- **Patch under a lock, and return only when patched.** The shipped guard marks a type patched before patching it, so
  a host that loses the race builds unpatched: 9 of 10 hosts enabling at once had their first build untracked (F23,
  RUN). The shape check and `Harmony.Patch` run under one lock, and `Enable…` returns after the patch is in place; that
  also keeps a shape from being patched twice, which Harmony would allow (each `Patch` call appends a prefix, E3). With
  the lock: 10 of 10 tracked, one prefix per shape.
- **Never unpatch.** `Reset()` and `Disable…` clear tracking state only. Unpatching cannot restore the shared code
  (`probe-harmony6`), and a patch that builds Confluent's own client for an untracked pair is harmless. The docs say
  the patch stays for the process.
- **The internal constructors** `Producer(ProducerBuilder<K,V>)`, `Producer(DependentProducerBuilder<K,V>)` and
  `Consumer(ConsumerBuilder<K,V>)` have the same signatures in Confluent.Kafka 2.0.2, 2.15.1 and 2.16.0-RC1 (READ:
  `Producer.cs:551`/`:565`, `:556`/`:570` and `:661`/`:692`) and in the released 2.14.0, 2.15.1 and 2.16.0 (RUN,
  reflection, audit `B/` E7), where every `Build()` is the same seven bytes of IL (`ldarg.0; newobj; ret`), so
  replacing it loses nothing. `DependentProducerBuilder` was unchanged from 1.0.0 to 2.15.1; 2.16.0 adds four
  experimental serializer-builder properties, which the internal constructor resolves (READ, RC1 `Producer.cs:661-690`),
  so a prefix that calls the constructor keeps them. The constructors are looked up when tracking is enabled; if one is
  missing, `Enable…` throws `NotSupportedException` naming the Confluent version and the alternatives
  (`.BuildTracked()`, DI decoration), rather than patching (Q3). The packages float on `Confluent.Kafka 2.*` and
  `Lib.Harmony 2.*` (their csprojs; the test projects inherit them), so CI builds against the newest of each: 2.16.0
  (no 3.x exists) and Lib.Harmony 2.4.3 (published 2026-10-11) on 2026-10-12, on which F1 and F2 reproduce exactly as
  on 2.4.2 and quick JIT off still holds (RUN, audit `B/`). The same float raises every consumer's floor at each pack
  (#97): 4.10.0 and later require Confluent.Kafka 2.16.0 or later, so the #135 reporter, on 2.15.1, cannot take R0
  without moving Confluent, and the next pack will require Lib.Harmony 2.4.3 (section 8). Taking #97's fixed floors
  (D21) would end CI's early warning unless a newest-versions lane stays.
- **Tier-up (F2).** No in-process fix held (`tiermit/`). R0 makes the loss visible and gives the remedy. `Enable…`
  reads the settings the way the runtime does (RUN, audit `B/` E5, ten cases): for each of `TieredCompilation` and
  `TC_QuickJit`, the `DOTNET_` variable, then the `COMPlus_` one, each read as a hexadecimal number from its leading
  digits (an optional `0x`; no digits means unset), and only then the runtimeconfig value
  (`AppContext.GetData("System.Runtime.TieredCompilation")` and `"…TieredCompilation.QuickJit"`, the string `"false"`
  when the csproj sets the property, otherwise null; `GetData` never shows the environment). The environment wins over
  the csproj (a csproj `false` with `DOTNET_TC_QuickJit=1` re-JITted), and the hex reading matters:
  `DOTNET_TC_QuickJit=false` starts with the digits `fa` and turns quick JIT on, overriding the csproj, while `true` is
  ignored. A check that read the runtimeconfig first, or took `false` for off, judged 4 of the 10 cases wrong. When
  tiering can re-JIT the patched method, it writes one line to standard error and registers a report diagnostic:
  "Kafka build interception can stop after the first few dozen builds under tiered compilation (about 40 in a test run
  with pauses); add `<TieredCompilationQuickJit>false</TieredCompilationQuickJit>` to the test project".
  `DiagnosticKind.CaptureDegraded` already exists (`DiagnosticEntry.cs:46`, "capture health handed in by the host"), so
  no public member is added. Its
  only channel today is `IngestRequest.HostDiagnostics` (`IngestPipeline.cs:263`), which an in-process run never builds:
  an in-process report takes its entries from `ReportDiagnosticsScope.Current` at generation
  (`ReportGenerator.cs:472`), long after `Enable…` ran. So core gains a small internal registry that generation merges
  into that list, and `Kronikol.csproj` an `InternalsVisibleTo` for `Kronikol.Extensions.Kafka.BuildInterception`, in
  the item group with the others (an `InternalsVisibleTo` in a property group is silently ignored).
  Measured cost of that setting at 4.9.0: the 126 facts `Kronikol.Tests.Kafka` had then (139 since #136) went from
  1.66 s to 2.28 to 2.54 s wall (in-test time 60 to 108 ms). Q1 asks whether R1 should also ship it as the package's
  default.
- **The build-interception test project sets the property itself**, so its facts, which build hundreds of clients,
  cannot flake on tier-up.
- **Ask 1 (R1).** `EnableProducerTracking<K,V>` patches `DependentProducerBuilder<,>.Build()` with the same prefix
  (internal constructor `Producer(DependentProducerBuilder<K,V>)`). A dependent producer may borrow a tracked owner's
  `Handle`: the wrapper's `Handle` delegates (`probe-harmony4`). Its delivery reports run on the owner's poll thread,
  which R0's call-time capture (4.2) already handles.

### 4.2 Producer and consumer defects (R0)

- **Capture at the call (F4).** `TrackingKafkaProducer` resolves the test identity, the phase, whether the phase is
  tracked and at which verbosity, the content and the request's timestamp on the caller's thread, before calling
  Confluent; `PhaseConfiguration.ShouldTrack` and `GetEffectiveVerbosity` read `TestPhaseContext.Current` themselves
  (`PhaseConfiguration.cs:15-36`), so on the poll thread they would read the wrong one. The delivery handler only adds
  partition, offset and outcome. Both halves are stored together then, the request's `Timestamp` preset to the call
  (`RequestResponseLogger.Log` keeps a preset one, `RequestResponseLogger.cs:59-60`, so core needs no new overload):
  storing the request at the call would leave Raw verbosity's label and URI without the partition and offset they hold
  (`KafkaOperationClassifier.cs:12-15`, `:44-47`), and would let `flow` nest any consume recorded meanwhile under the
  still-open produce (RUN, audit `C/probe-output.txt` P8, case S5). `KafkaTracker` gains an internal overload that takes
  what was captured (the wrappers and the tracker share an assembly); Q17's public `TrackProduce` is the same path. The
  gRPC interceptor's stream outcome (4.13.2, `GrpcTrackingInterceptor.cs:383-441`) captures at the call and logs once
  at the end in the same way.
- **Failures are drawn (F5, F6).** A delivery report whose `Error.IsError` and a `ProduceAsync` that throws log the
  request and a failure response, on the convention 4.4.1 set for a call that did not answer (`FailedSend`,
  `FailedSend.cs:13-30`, used by the HTTP and Cosmos handlers): a status behind a bang, which the diagram draws as
  recorded (`PlantUmlCreator.cs:1156-1158`) and `InteractionStatus.IsError` reads as an error, and the message chain
  from `FailedSend.Describe` in `Error`. `FailedSend.Status` would write the exception's type name, which for
  `ProduceException<K,V>` is ``!ProduceException`2`` and says nothing; Confluent's error code says what happened, so
  the status is `!{Error.Code}` (`!Local_MsgTimedOut`) for a failed delivery report and for a thrown
  `ProduceException`/`KafkaException` (both carry an `Error`), and `FailedSend.Status(ex)` for anything else. `Error`
  is `FailedSend.Describe(ex)` when there is an exception and `report.Error.Reason` when there is not. The exception is
  rethrown and the caller's handler still runs. The wiki's two sentences that define the bang status as the exception's
  type (`Generated-Reports.md:298`, `Exporting-to-OpenTelemetry.md:32`) gain Kafka's form (6.4). A successful produce
  keeps no status, so `InteractionStatus.IsError` still reads it as a success (a status word outside its
  `NonErrorStatuses` list would count every produce as an error, #140).
- **Every failing path (F22).** A synchronous `Produce` that throws (`Local_QueueFull`, an unknown topic, a serializer)
  is drawn the same way at the call, then rethrown. A `Consume` that throws `ConsumeException` is drawn as a failed
  consume from the record the exception carries (topic, partition, offset, and its headers for the identity, so it is
  the message's test's), then rethrown.
- **A cancelled `ProduceAsync`.** Confluent registers the token and queues the message anyway
  (`Producer.cs:800-818`; RUN, audit P4b: still queued after the cancel), so the caller's wait failed while the message
  may still be delivered and consumed. It is drawn as the HTTP handler draws a call its caller cancelled, by
  `FailedSend` (`!TaskCanceledException`), with `Error` adding that the message was queued and may still be delivered,
  so the consume that can follow does not contradict it.
- **No handler where Confluent will not call one (F7, F24).** Whether the producer reports deliveries is read from its
  configuration, not learned from an exception: the prefix reads the builder's `Config`
  (`dotnet.producer.enable.delivery.reports`; `ProducerBuilder<,>.Config` is `protected internal`, found by reflection on
  the builder and on a subclass), and a wrapper given Confluent's own `Producer<,>` reads its private
  `enableDeliveryReports`; for a dependent producer the setting is its owner's, read through `Handle.Owner` (internal,
  2.14.0 to 2.16.0; `Handle.Config` is public from 2.16.0), because the dependent's own field says true while the owner
  registered no delivery callback, so its handlers never run (F24). With reports off, the wrapper produces without a
  handler and logs at the call (no partition or offset). Only when the setting cannot be read (a foreign `IProducer`
  under `.Tracked()` or DI decoration, or a Confluent version whose private field moved) does it fall back to
  Confluent's refusal, narrowed to an exception of exactly type `InvalidOperationException` carrying Confluent's
  message ("A delivery handler was specified, but delivery reports are disabled."), for a call where the caller passed
  no handler, retried once without one and remembered: a bare `catch (InvalidOperationException)` would also catch
  `ObjectDisposedException`, which derives from it, and any decorator's own refusal, and a decorator's work before the
  refusal runs twice (RUN and READ, audit `B/` E6; Confluent refuses before anything is queued, `Producer.cs:874-877`,
  and its produce paths throw no other `InvalidOperationException`).
- **Durations (F8).** A produce is stamped at the call (request) and at delivery (response), so its `durationMs` is
  the broker round trip. Entries logged at one instant (consume, subscribe, commit, flush, transactions, the public
  `Log…` methods) carry one timestamp on both halves, which the data files read as "measured nothing", as `LogPair`
  does since 4.7.3.
- **Headers (F9): the caller's `Message` is never written to.** The wrapper hands Confluent a copy: the caller's
  `Key`, `Value` and `Timestamp`, and new `Headers` holding the caller's headers in order, then each identity header
  the caller's lack. That is the rule 4.12.1 set for gRPC, whose interceptor copies the caller's `Metadata` because a
  reused one gained a `traceparent` per call (`GrpcTrackingInterceptor.cs:590-634`), and the rule the HTTP handler
  keeps with `AddIfAbsent` ("a retry is not stamped twice", `TestTrackingMessageHandler.cs:223-230`). The plan's first
  draft removed the old headers and added new ones on the caller's message (`Headers.Remove`), which Kronikol4J does
  (`KafkaTestHeaders.stamp`); that still writes to an object the caller may reuse or inspect, deletes identity headers
  a caller set on purpose, and in R1 would leave a reused message's `traceparent` from its first produce on every
  later one, which then records the first produce's trace (4.3). With a copy, a reused message carries nothing of
  Kronikol's into its next produce. The log's content and headers are read from the caller's message, before anything
  is added, as gRPC logs the caller's metadata (`GrpcTrackingInterceptor.cs:543`). A caller sees no change on success:
  Confluent already hands back a message of its own in a delivery result or report (`Producer.cs:979-985`,
  `:1031-1039`; RUN, audit P4c); `ProduceException.DeliveryResult.Message` and the result with delivery reports off
  hold the object Confluent was given (`:767-772`, `:829-833`), now the copy, whose re-send keeps the same identity and
  trace. When Kronikol would add nothing, the caller's message is passed through as it is, so `Headers` stays null.
  `Timestamp` is copied as it is (Confluent refuses any type but `Default` and `CreateTime`, `:290-295`). A mocked
  inner producer that matches the message by reference stops matching. 4.11.0 (#136, `709b3afc`) made the same method resolve the
  identity through the tracker's accessor (`TestInfoResolver.ResolveWithSource(_tracker.HttpContextAccessor,
  _options.CurrentTestInfoFetcher)`, `TrackingKafkaProducer.cs:109`). R0 keeps that argument;
  #136's facts `TrackingKafkaProducerTests.ProduceAsync_injects_the_requests_identity_when_the_options_carry_an_accessor`
  and `Produce_injects_the_requests_identity_when_the_tracker_was_given_an_accessor` (`:274`, `:292`) hold it, read
  from the message the inner producer received. The call-time capture above uses the same accessor.
- **The background identity (F20, Q18).** `InjectTestIdentityHeaders` stamps whatever resolves, so with
  `CaptureBackground` on a produce made outside any scenario writes `Unknown`/`unknown` on the message; the consumer
  adopts it as a message identity (`TestIdentityScope.SetFromMessage`), its calls then resolve as attributed with
  source `Scope`, and `AutoCorrelateOnConsume` correlates their keys to `unknown`. gRPC stopped sending it in 4.13.0
  (`GrpcTrackingInterceptor.cs:568`, `TestIdentity.IsAttributed`, `TestInfoResolver.cs:16`); whether the HTTP handler
  and the messaging producers should stop is the gRPC plan's open Q3 (`GRPC_IDENTITY_PROPAGATION_PLAN.md:778-780`,
  `ROADMAP.md:955`). R0 rewrites this method, so Q18 asks whether Kafka adopts gRPC's rule there; the id a consumer
  then resolves is the same background id, with its true source.
- **A message's identity ends at the next `Consume` (F10)**, whatever the poll returns.
- **Labels and docs (F11, F12, F3, F4).** The Raw label is trimmed, for every operation without a topic; the
  classifier's summary, `Reset()`'s and `Disable…`'s docs say what they do; the two sentences #136 added that F4
  falsifies (`KafkaTrackingOptions.cs:70`, the comment at `TrackingKafkaProducer.cs:108`) become true with F4's fix and
  are re-read against it; `query trace`'s class summary stops claiming ids "on every interaction" (`QueryCommand.Trace.cs:6`, F16).
- **One tracker per enabled pair, registered when used (F19).** The interceptors build their trackers through an
  internal constructor that does not register, and register a tracker when it wraps its first client, so a tracker
  replaced by a later `Enable…` before any client used it, or a consumer tracker in an app that only produces, never
  reaches the "registered but never invoked" warning; `Enable…` with the options it already holds keeps its tracker.
  Its `ComponentName` names the direction and pair (`KafkaTracker (Kafka, producer <String,String>)`), so the warning
  and the diagnostic page tell them apart. `TrackingComponentRegistry` keeps its public surface. That the last
  `Enable…` sets every host's options is #160's (4.6).
- **Phase content (F13).** The wrappers build content unless every configured verbosity is `Summarised`; the tracker's
  variants already drop it per phase.

### 4.3 W3C ids and the Kronikol trace id (R1, ask 2)

| Entry | Trace and span id recorded |
|---|---|
| Produce, through the wrapper | The last `traceparent` the copy (4.2) carries after the inner call returns, which is the caller's own, the one an instrumentation wrapper inside Kronikol's appended (its send span, injected before its first await, `research-confluent.md` §7.4), or the one Kronikol added (below); else `Activity.Current` captured at the call |
| Consume, through the wrapper | The consumed message's last `traceparent`, and only when the consume is recorded under the test the message's identity headers name; otherwise none. Never the polling loop's `Activity.Current` (C5) |
| A hand-written log | `Activity.Current` at the call (the reporter logs inside the processing span). Q17's `TrackProduce`/`TrackConsume`, which take the message, follow the two rows above |

- **Why a consume records so little (RUN).** A consume recorded under another test than the producing one, or under
  the background, makes two tests claim the producer's trace. The audit's probe (`C/probe-output.txt` P2) measured
  test X's whole-test flow fall from 4 spans to 1 and its own POST's popup from 2 to 1: the whole-test flow takes a
  trace only when one test claims it (`InternalFlowSegmentBuilder.cs:74-78`), and the popup, for a trace two tests
  claim, takes the subtree of the call's span (`:152-162`), here the producing host's request span, whose 50 ms
  tolerance (`:230-234`) reached a span 39.6 to 46.2 ms before the consume; both tests then took that span and
  neither kept it (`:242-266`). A consume lands under another test whenever the consumer's framework fetcher answers
  another test, since in a flow that is not detached the resolver asks the fetcher before the message's scope
  (`TestInfoResolver.cs:91-114`), a host started inside a test and not detached being the usual case, or under the
  background. Requiring the message's identity to be the consume's keeps each trace one test's. The polling loop's
  `Activity.Current` is worse: inherited by every consume of every test, it would put them all on one trace.
- **Injection, only where nothing else can inject.** Kronikol adds a `traceparent` only when its wrapper is the last
  before Confluent (`_inner` is Confluent's own `Producer<,>`), the analogue of the HTTP handler injecting only when
  the transport will not (`TestTrackingMessageHandler.cs:164-172`, `TransportPropagatesTraceContext` `:348-358`).
  Then a copy with no `traceparent` gets `00-{trace}-{span}-{flags}` from `Activity.Current`, flags `01` when it is
  recorded (as gRPC since 4.12.1, `GrpcTrackingInterceptor.cs:529-535`, `:624`, and HTTP since 4.14.3,
  `TestTrackingMessageHandler.cs:171`), or random ids with flags `00` when no `Activity` is current (as both do,
  `:178-182`). When another wrapper sits inside Kronikol's, the usual shape on the DI path
  (`KafkaServiceCollectionExtensions.cs:31-34` decorates whatever `IProducer` is registered), Kronikol injects nothing
  and records what that wrapper wrote: injecting first, as the first draft did, made `Confluent.Kafka.Extensions.Diagnostics`,
  whose consumer reads the first `traceparent` (`ActivityDiagnosticsHelper.cs:91-92`), parent on Kronikol's header
  rather than its own publish span, changing the trace Kronikol reports on. Kronikol never adds a second
  `traceparent`. It sits under a new `KafkaTrackingOptions.PropagateTraceContext` (Q5).
- **Flags in practice.** Where only Kronikol listens, `Activity.Recorded` is false (the listener samples `AllData`,
  `InternalFlowActivityListener.cs:49-50`), so an injected header says `00` unless the app's own tracing records the
  span. A fact pins `01` under a recording listener (T47), so "always `00`" cannot pass unseen.
- **Parsing.** `ActivityContext.TryParse` (in every target framework), which rejects upper case, version `ff`, a zero
  id and trailing text, as the runtime does when it parents; not a parser of Kronikol's own. The first draft
  lower-cased upper case, which no .NET consumer would parent from, and refused version `01`, which the runtime
  accepts. ProxyTap's public `TraceParent.TryParse` (`ProxyTapOptions.cs:183-195`) accepts all four of the runtime's
  refusals (section 8).
- **The Kronikol trace id (F21, Q19).** The produce takes the trace id of the request being served, as gRPC's
  `InboundTraceId` does (`GrpcTrackingInterceptor.cs:571-588`); the copy carries it in a third message header beside
  `kronikol-test-name` and `kronikol-test-id` (`TestTrackingMessageHeaders`, `HttpHeaders.cs:19-23`, a new public
  constant), added only if absent and under `PropagateTestIdentity`; the consume takes it. `flow` then nests a delivery
  under the call that published it while that call is open (RUN P8, case S3); the consumer's own calls still start
  trace ids of their own unless R2 holds the consume open (case S4).
- **The listener (F14).** The trackers start `InternalFlowActivityListener` on first use, as the HTTP handler and gRPC
  interceptor do (an `InternalsVisibleTo` for `Kronikol.Extensions.Kafka`), so a Kafka-only suite captures spans;
  T31 can read `InternalFlowActivityListener.IsStarted` (`:86`).
- **What changes for readers.** `query trace` includes Kafka calls; a consume joins its producer's trace when it is the
  message's test; OTLP export moves Kafka spans from the per-test trace into the app's trace
  (`Exporting-to-OpenTelemetry.md:64-67`, a visible change the changelog names); the whole-test flow gains the
  handler's spans only for a consumer that parents on the message (C4); a consume's `activitySpanId` names the
  producer's span, not one of its own, which the docs say (6.4); subscribe, commit, flush and transaction entries get
  no ids.

### 4.4 Message content (R1, ask 3 and #167)

Two shapes are on the table, and Q16 chooses:

- **(A) The first draft's envelope.** `KafkaTrackingOptions.ContentFormat`, an enum `KafkaContentFormat { Text, Json }`,
  default `Text` (today's line); `Json` writes `{"key": K, "value": V}` with the value embedded as JSON, so
  `kronikol query values <report> --request --path value.amount` reaches it.
- **(B) #167's shape, recommended.** No option. The body is the value; the key goes in a reserved entry header
  (`kafka-key`), followed by the message's own headers; the note draws the key line above the body whatever the header
  toggle says. That last part matters: headers are hidden by default since 4.0.0 (`ReportToggleDefaultsResolver.cs:19`),
  so a key in the header block alone would vanish from a default report, where today's line shows it.

Why (B):

- #167's own query, `values --service Kafka --path '$.Status'`, works as written (with `--request`, Q11); under (A) it
  becomes `$.value.Status`. Under (A) `diff --body` also compares every message's key, often a new GUID per run.
- Shipping (A) with default `Text` and moving to (B) later would change a public option's default, which CLAUDE.md
  counts as major. (B) adds no option.
- Today's readers already serve (B). `RequestResponseLog.Headers` is `(string, string?)[]`; `kronikol query http
  --headers` prints any entry's headers (`QueryCommand.Payloads.cs:339-347`); a note draws headers as gray lines,
  which the YAML view skips (`collapsible-notes-script.js:1021-1031`); `values`, `--where` and `diff --body` read the
  body. A body cut at `MaxContentLength` stops parsing, as an HTTP body does.
- It is what Kronikol4J's open Kafka note would then have to follow (6.5), and it answers Q9: the message's headers are
  logged, read from the caller's message before Kronikol adds its own (as gRPC logs the caller's metadata), and on a
  consume Kronikol's own headers are left out.
- Semver: it changes what a Kafka note shows by default, a change to generated report output, which CLAUDE.md does not
  count as major on its own; R1 is a minor for its new surface anyway, and its changelog names the change.

(B)'s rules, for the body (the value) and the `kafka-key` header (the key), each written only when
`LogMessageValue`/`LogMessageKey` is on, and neither under `Summarised`:

| Value or key | Written as |
|---|---|
| `string` | as is: a JSON value is then indented in the note, offered the YAML view and queryable |
| `byte[]` | as the HTTP handler writes a body (`HttpContentReader.cs:40-72`): UTF-8 text unless it is binary, else the JSON documents it holds, else `[binary content]` |
| number, `bool`, `Guid`, `DateTime`, `DateTimeOffset` | its invariant text (JSON has no date type, which the first draft's table missed) |
| a Google.Protobuf `IMessage` | `JsonFormatter.Default.Format`, found by reflection (no package reference) (Q10) |
| anything else | `TrackingSafeSerializer` (`TrackingSafeSerializer.cs`), the serializer the other trackers use |
| null, Confluent's `Null` or `Ignore` | nothing |

A message header's value is its bytes as UTF-8 text when they decode strictly, else `[binary content]`. The emitter
change (`PlantUmlCreator` draws `kafka-key` outside the header block) and the page change (the header toggle leaves the
line, `reconstructNoteJson` still skips it) need Playwright facts (T60) and a Kronikol4J ledger line. If Q16 picks (A),
the first draft's table and facts T32 to T36 stand instead, and Q9 (no headers in the envelope) with them.

### 4.5 The arrow popup (R2, opt-in, only if Q6 says so)

R1's ids put the handler's spans in the scenario's whole-test flow only for a consumer that parents on the message, and
never in the consume arrow's popup (C4). R2 gives a consume a span of its own and a window that covers the handler.

- **A scope first.** `KafkaTracker.TrackConsume` (Q17) returns an `IDisposable`: it starts a `Kronikol.Kafka`
  consumer `Activity` (kind `Consumer`, `{topic} process`) parented by the message's `traceparent`, makes the
  message's identity current, logs the consume's request, and at dispose logs its response and stops the span. With
  `using var scope = tracker.TrackConsume(result);` around the handler, the spans the handler starts are the span's
  children and lie inside the window, whatever thread or task runs the loop. The wrappers' tests and the wiki lead with
  it (Q7's `BeginConsume` is this method).
- **Zero-change, best effort.** With `KafkaTrackingOptions.TraceMessageProcessing` on, the wrapper's `Consume` opens
  that scope and leaves it open: the span stays current when `Consume` returns (a synchronous method's `AsyncLocal`
  change reaches its caller), and the scope closes at the consumer's next `Consume` (at its start, before it blocks),
  `Commit`, `StoreOffset`, `Close` or `Dispose`. The audit's probe found where that fails
  (`C/probe-output.txt` P3, `Activity.cs:743-762`: `Stop()` restores whatever was current at `Start`): `await
  Task.Run(() => consumer.Consume(ct))`, a common hosted-service shape, leaves nothing current for the handler; a loop
  that wraps each `Consume` in an `Activity` of its own, or two consumers in one loop, parents the next span on a
  stopped one; an `Activity` the handler leaves current is dropped. So the scope restores `Activity.Current` to what
  it was when the next call began unless that was its own span, the wiki names the `Task.Run` shape, and a fact pins
  each case (T62).
- **What is captured, and when.** The deferred response reuses the identity, phase, verbosity and ids captured with
  the request, since F10 clears the message's identity at that same next `Consume` and the tracker re-resolves on
  every call today (`KafkaTracker.cs:151`). A `Commit` or `StoreOffset` from another thread (a commit timer) closes
  the scope on that thread; the wiki says so.
- **The consume's own span anchors the popup.** The consume entry's span id is the `Kronikol.Kafka` span's, not the
  producer's (4.3), so the popup takes the handler's spans by subtree, not the producer's by the 50 ms tolerance (the
  audit measured the producer's `db insert` entering the popup when the entry carried the producer's span).
- **The publish span.** A produce gets a `{topic} publish` producer span the same way, so its `traceparent` names a
  span that exists.
- **Sources and sampling.** `Kronikol.Kafka` joins the well-known sources (`InternalFlowSpanCollector.cs:11-27`), so
  `AutoInstrumentation` keeps the trace (`:73-90`), as `Kronikol.Grpc` is kept. Its spans are sampled `AllData`, not
  recorded, so an app whose own tracing samples by parent drops the handler's child spans from its exports unless it
  listens to `Kronikol.Kafka`; the wiki says so.
- The diagram draws the handler's calls between the consume and its return, `flow` nests them under it
  (`CallNesting.cs:62-76`; RUN P8, case S4), and `durationMs` becomes processing time. A handler that throws out of
  the loop leaves the response to `Close` or `Dispose`, so its duration runs to shutdown; the scope form has no such
  case.

Off by default: it changes the app's trace structure and the diagram's order. Q6 also lists a builder rule as the
alternative (a call whose own span is stored takes that span's subtree, whatever its time).

### 4.6 What stays untracked, and what the wiki says about each

- **Interception after tier-up** with quick JIT on: a diagnostic and the remedy (4.1).
- **A dependent producer of a pair that is not enabled**: untracked, as for the owned builders.
- **Which host built a client.** Interception is process-wide: one tracker and one set of options per key and value
  type (`KafkaTrackingInterceptor.cs:22-23`), so in a process with ten hosts every intercepted producer of a pair is
  drawn with that pair's `CallerName`, whichever host built it (measured: ten hosts each enabling the same pair with
  its own caller name, and every host's entries carried the last one's, audit `B/` E3, E8), and a library that builds its clients behind its own
  interfaces can only be decorated by re-implementing the wrappers. #160 asks for both; it is not in this plan
  (section 8, Q17). Ask 1 is still worth shipping on its own: it is what a single-host suite, or one whose hosts
  share a caller name, needs.
- **A message produced without `traceparent` by a producer Kronikol does not track**: its consume records no ids, so
  the chain breaks there.
- **A consume recorded under another test than its message names** (a consumer whose framework fetcher answers
  another test): no ids, so it cannot take spans from the producing test (4.3). Its attribution is the defect; the
  wiki's background-attribution pages give the remedy (detach the host).
- **The handler's spans in the message's trace** when the consumer links rather than parents (OpenTelemetry's contrib
  package) or has no instrumentation: not there before R2 (C4).
- **Two `traceparent` headers on one message** are never Kronikol's doing (4.3): with an instrumentation wrapper inside
  Kronikol's, the wrapper alone injects, and Kronikol records the last header the copy carries, which is the
  wrapper's. A message whose caller already put a `traceparent` on it and whose inner wrapper appended another carries
  both; OpenTelemetry's contrib package reads the last (`OpenTelemetryConsumeResultExtensions.cs:200-206`) and
  `Confluent.Kafka.Extensions.Diagnostics` the first (`ActivityDiagnosticsHelper.cs:91-92`), so the two disagree there,
  and Kronikol reads the last. Pinned by T54.
- **A consumer that hands each message to a worker** (R2): the consume ends at the next poll, so the window misses
  the worker's later spans; `query trace` and the whole-test flow still have them. The scope form (4.5) has no such
  limit.
- **Partition and offset** of a produce with delivery reports off.
- **The order a produce draws in.** Both halves are stored at delivery (4.2), and the diagram draws in store order
  (`PlantUmlCreator.cs:103-110`), so a produce draws where its delivery report arrived, while its request's timestamp
  is the call's; the data files carry both.

## 5. Tests, red first

New classes: `KafkaBuildInterceptorPairTests` and `KafkaBuildInterceptorTierTests` (build-interception project, which
gains `<TieredCompilationQuickJit>false</TieredCompilationQuickJit>` and targets net8.0 as well as net10.0),
`TrackingKafkaProducerFailureTests`, `TrackingKafkaProducerContextTests`, `KafkaTraceContextTests`,
`KafkaMessageContentTests`, `KafkaTrackerHelperTests` and the broker facts (5.4) (Kafka project). Classes that touch
the correlation store or the component registry join the collections the project's do (`TestCorrelationStore`,
`TrackingComponentRegistry`), which `ProcessWideTrackingStateTests` guards. Facts that read the caller's message
after a produce (`TrackingKafkaProducerTests.cs:221`, `:240`, `:255`, and #136's `:274`, `:292`) are rewritten to read
the message the inner producer received (4.2): the fact at `:240` (`Assert.Null(message.Headers)` with only
`PropagateTestIdentity` off) would turn red under R1's default `PropagateTraceContext` if the first draft's in-place
injection had shipped. Fakes stay hand-written, as the project's are; one shared fake
producer replaces the four copies (`KafkaServiceCollectionExtensionsTests.cs:297`, `KafkaTrackingInterceptorTests.cs:241`,
`TrackingKafkaProducerFactoryTests.cs:111`, `TrackingKafkaProducerTests.cs:308`), records every message it is given
(today's discards it), and gains a delivery handler it can invoke on a thread started under another `AsyncLocal`
context, to reproduce F4 without a broker.

Where facts live: an entry-level fact in `Kronikol.Tests.Kafka` reads the process-wide log filtered by its own test id,
as that project's facts do today (`TrackingKafkaProducerTests.cs:10-24`); its timestamps answer T15 without a report.
A fact that generates a report (T31's whole-test flow, T43's byte-identity, T55's internal flow, T57's `flow`) must not read other tests' calls, which a
run since 4.12.2 lists as an UnattributedInteractions diagnostic: it lives in `Kronikol.Tests`, which can open
`RequestResponseLogger.IsolateForTests()` (internal, `RequestResponseLogger.cs:93-107`), or the Kafka project gains
an `InternalsVisibleTo`. Neither Kafka test project can see core's internals today.

### 5.1 R0

Red runs are against the release published before R0 (4.14.6 when this audit measured; v4.14.9, whose Kafka code is
the same, was the newest tag at its end).

| # | Fact | Red on the previous release because | Slice |
|---|---|---|---|
| T1 | With `<string,string>` enabled, `ProducerBuilder<Null,string>.Build()` is an `IProducer<Null,string>` and `Produce` does not throw (F1) | it is a `TrackingKafkaProducer<String,String>` and throws `EntryPointNotFoundException` | S1 |
| T2 | The same for `<string,byte[]>` producers and `ConsumerBuilder<Null,string>` (`Subscribe` works) (F1) | wrong type, throws | S1 |
| T3 | `<string,string>` and `<string,byte[]>` both enabled: each builder returns its own pair's tracker (F1) | the last patch wins for both | S1 |
| T4 | After `Reset()`, every pair builds its own `Producer<K,V>`, untracked; enabling again tracks (F1, F3) | `<Null,string>` stays `Producer<String,String>` | S1 |
| T5 | `DisableProducerTracking<string,string>`: that pair untracked and correctly typed, another enabled pair still tracked (F1) | wrong types | S1 |
| T6 | A value-type pair enabled alone (`<int,string>`) is tracked and leaves `<string,string>` alone (guard) | passes: the two shapes have their own code | S1 |
| T7 | A builder subclass whose `Build()` calls `base.Build()` returns one wrapper, of its own pair, generic or not (`class OrdersBuilder : ProducerBuilder<Null,string>`) (guard) | wrong type when its pair is not `<string,string>`; the prototype's mechanism throws `ArgumentException` for the non-generic one (E2b) | S1 |
| T8 | With the test project's quick JIT off, 400 builds (each disposed) are all tracked (the remedy, F2) | passes on 4.9.0 under the same setting; a guard that the prefix keeps the remedy working | S1 |
| T9 | The settings reader, fed the ten cases the audit measured (environment and runtimeconfig, `0`, `1`, `false`, `true`, `0x0`, `DOTNET_` beside `COMPlus_`), gives the verdict the runtime obeyed in each; with quick JIT on, enabling writes the line and the run's diagnostics carry one `CaptureDegraded` entry naming the remedy; off, none (F2) | no such line (compile stub) | S1 |
| T10 | A missing internal constructor (lookup stubbed) makes `Enable…` throw `NotSupportedException` and patches nothing (Q3) | compile stub | S1 |
| T11 | `Produce(…, handler)` called under test B on a producer built under test A: both entries are B's (F4) | both are A's | S2 |
| T12 | A delivery report with an error is drawn as a failure: status `!Local_MsgTimedOut`, `Error` the reason; the caller's handler still runs (F5) | logged as an ordinary produce | S2 |
| T13 | `ProduceAsync` that throws `ProduceException`: request and failure response logged (status from its error code, `Error` from `FailedSend.Describe`), exception rethrown, both overloads (F6) | nothing logged | S2 |
| T14 | A producer built with delivery reports off: plain `Produce` succeeds and is logged once, at the call, its setting read from the configuration; a foreign inner producer that refuses handlers takes the narrowed fallback once; an `ObjectDisposedException` from the inner is rethrown, not taken for a refusal (F7) | `InvalidOperationException` | S2 |
| T65 | A tracked dependent producer whose owner has delivery reports off logs each produce at the call; its owner's setting on, at delivery (F24) | nothing logged at all (RUN, audit E6 iii) | S2 |
| T15 | A produce's request is stamped at the call and its response at delivery, both stored at delivery; a consume's two halves carry one timestamp, so the data files give it no `durationMs` (F8) | two different instants, invented duration | S2 |
| T16 | Produce and `ProduceAsync` leave the caller's `Message` as it was (no header added; `Headers` still null when it was null); the message the inner producer received carries one identity pair, naming the produce's test; produced twice, each inner message names its own produce's test; an identity header the caller set is kept (F9) | the caller's message gains a pair per produce | S2 |
| T17 | After a message from test A, an empty poll (and, separately, an EOF) ends A's identity on the consumer's context (F10) | A's identity remains | S2 |
| T18 | Raw label of every operation without a topic is its name alone: `Commit`, `Flush`, `Unsubscribe` and the five transaction operations (F11) | `"Commit "` | S2 |
| T19 | `Verbosity = Summarised`, `ActionVerbosity = Detailed`: an action-phase produce and consume carry content (F13) | null content | S2 |
| T20 | The shipped XML docs: `Reset()` does not say it removes patches, `KafkaOperationClassifier` does not say HTTP (F3, F12), with the existing reader of shipped docs | the old sentences | S3 |
| T21 | Coverage (F17): `ProduceAsync(TopicPartition, …)`, `Consume(int)` and `Consume(CancellationToken)`, null and EOF results skipped, one log per topic for `Subscribe(IEnumerable<string>)`, `AutoCorrelateOnConsume` with and without `ConsumeKeyExtractor`, `TrackDuringSetup = false`, every `IProducer`/`IConsumer` member delegates | pass where the code is right; each is mutation-checked (5.3) | S2 |
| T37 | A builder Confluent refuses throws from the patched `Build()` exactly what it throws unpatched, type and message: an unknown property (`InvalidOperationException`), `acks=banana` (`ArgumentException`), a bad boolean (`FormatException`), a key type with no serializer (`ArgumentNullException`) | the prototype's reflection call wraps it in `TargetInvocationException` (red on a build of the prototype's mechanism; the shipped postfix lets `Build()` throw as it is) | S1 |
| T38 | With `<int,string>` enabled, `ProducerBuilder<int,byte[]>`, `<int,Null>` and `ConsumerBuilder<int,byte[]>` give their own pair's client, and it works; with `<string,int>` enabled, so do `<Null,int>` and `<byte[],int>`; `<KeyValuePair<Uri,Uri>,string>` beside an enabled `<KeyValuePair<string,string>,string>` too; an enabled `<long,string>` is tracked (its shape patched by its own `Enable…`) (F18) | each returns the enabled pair's wrapper and throws `EntryPointNotFoundException` (RUN, audit E2a) | S1 |
| T39 | Ten threads enable pairs of one shape at once, then each builds: every first build is tracked, every client is its own pair's, and the shape carries one prefix (F23) | 9 of 10 first builds untracked (RUN, audit E3) | S1 |
| T45 | With `CaptureBackground` on, a produce made outside any scenario carries no identity headers, and its consumer resolves the background with source `None`, storing no correlation (F20, Q18) | the message carries `Unknown`/`unknown` and the consume resolves `Scope` | S2 |
| T46 | Enabling a pair twice, or `EnableTracking` in an app that only produces, leaves no tracker the run would report as never invoked, and each registered tracker's name says its direction and pair (F19) | 2 registered and 1 unused, all named "KafkaTracker (Kafka)" (RUN, audit E8) | S1 |
| T48 | A synchronous `Produce` that throws (the queue full, a serializer's error) is drawn as a failed produce at the call, status from the error code, then rethrown (F22) | nothing logged | S2 |
| T49 | A `Consume` that throws `ConsumeException` is drawn as a failed consume of the record's topic, partition and offset, under the test its headers name, then rethrown (F22) | nothing logged | S2 |
| T50 | A `ProduceAsync` whose token is cancelled is drawn with `!TaskCanceledException`, and its `Error` says the message was queued and may still be delivered | nothing logged | S2 |
| T51 | A consume recorded between a produce's call and its delivery is not nested under the produce by `flow` (both halves of a produce are stored at delivery) | the first draft's design (request stored at the call) nests it, RUN P8 S5; a guard on today's code | S2 |
| T52 | A produce called in the setup phase and delivered after the action phase began is a setup entry, at setup verbosity (F4) | the phase is read on the poll thread | S2 |

### 5.2 R1 (and R2)

| # | Fact | Red on R0 because | Slice |
|---|---|---|---|
| T22 | `new DependentProducerBuilder<string,string>(owner.Handle).Build()` is tracked; `<Null,string>` dependent is correctly typed; a value-type dependent pair is tracked when enabled (ask 1) | untracked | S4 |
| T23 | A dependent producer's `Produce(…, handler)` is its caller's (ask 1, F4) | untracked | S4 |
| T24 | Under an ambient `Activity`, `Produce(…, handler)` records its trace and span id, taken at the call, not in the handler, which runs on the poll thread under another context (ask 2). (`ProduceAsync` is not a fact: its continuation gets the caller's `Activity.Current` back, so reading it there is indistinguishable) | null | S5 |
| T25 | Inner client Confluent's own, message without `traceparent`, no `Activity`: the copy gets one (flags `00`) and the entry records its ids, the caller's message none; with `PropagateTraceContext = false`, none is added (ask 2, Q5) | none added | S5 |
| T26 | A message that already carries `traceparent`: sent as it was, and the entry records its ids (ask 2) | null | S5 |
| T28 | A consumed message with `traceparent`, consumed under the test its identity headers name: the consume entry records its trace and parent span; `query trace` on the produce's id lists produce then consume (ask 2) | null; `trace` finds only the produce | S5 |
| T30 | `LogProduce`/`LogConsume` called by hand record `Activity.Current` (ask 2) | null | S5 |
| T31 | A scenario whose consumer runs on its own flow (not the test's): with a consumer that parents on the message (as `Confluent.Kafka.Extensions.Diagnostics` does), the handler's spans are in the scenario's whole-test flow; with one that links (as OpenTelemetry's contrib package does), they are not; `InternalFlowActivityListener.IsStarted` once a Kafka tracker has run (ask 2, F14) | no listener, no ids, no spans; a handler on the test's own flow would pass trivially, which is why the consumer has its own | S5 |
| T47 | An injected `traceparent` says `01` under a listener that records the span and `00` under Kronikol's alone (4.3) | none injected | S5 |
| T54 | An instrumentation-style wrapper inside Kronikol's that appends its own `traceparent`: Kronikol injects none, the inner message carries exactly the wrapper's, and the entry records it; with the caller's own `traceparent` too, the entry records the last (4.3, 4.6) | null | S5 |
| T55 | A consume recorded under another test than its message names (a fetcher answering another test), or under the background, records no ids, and the producing test's whole-test flow and POST popup keep every span they show without Kafka ids (the audit's P2 shape); under the message's own test it records them (4.3) | guard on R0, which records no ids; red against the first draft's rule, RUN P2 | S5 |
| T56 | A consume of a message with no `traceparent`, under an ambient `Activity` in the polling loop, records no ids (4.3) | guard on R0; red against the first draft's fallback | S5 |
| T57 | A produce made while a host serves request X carries X's Kronikol trace id on the message, the consume records it, and `flow` nests the consume under X while X is open (Q19, F21; RUN P8 S3) | a new trace id per entry; the consume at the top level | S5 |
| T58 | `traceparent` parsed as the runtime parses it: version `01` with a longer value accepted; upper case, version `ff`, a zero trace or span id and trailing text refused; the entry then falls back (a produce to `Activity.Current`, a consume to none) (ask 2) | null either way; the parse rules are new | S5 |
| T59 | Under Q16 (B): the body is the value (a JSON string indented and queryable, `values --request --path '$.Status'` returns it); `kafka-key` and the message's own headers are the entry's headers, Kronikol's own not among them; a binary `byte[]` is `[binary content]`; a `Null` key, `LogMessageKey = false`, `LogMessageValue = false` and `Summarised` each drop their part (ask 3, #167) | `Key: …, Value: …` | S6 |
| T60 | Playwright: a Kafka note shows the key line above the indented body with the header toggle at its default (hidden) and shown, the headers below it follow the toggle, and the YAML view is offered and flips the body (ask 3, #167); the page's text read as painted, not its markup | the note is one text line | S6 |
| T61 | A hand-written decorator built on `TrackProduce`/`TrackConsume` gets T11's caller attribution, T12's failure, T16's untouched message, T24's ids and T45's identity rule, asserted the same way (Q17) | the helpers do not exist (compile stub) | S6 |
| T32-T36 | Only if Q16 picks (A): the first draft's envelope facts (embedded JSON and `values --request --path value.a`; `byte[]`, `Null` key, `LogMessageKey = false`, `Summarised`; protobuf; default `Text` unchanged; Playwright for the `Json` note) | `Key: …, Value: …` | S6 |
| T40 | R2: inside `TrackConsume`'s scope, `Activity.Current` is a `Kronikol.Kafka` span parented by the message's `traceparent`, the spans the handler starts are its children, and the consume entry's span id is that span's | no span | S9 |
| T41 | R2: the consume's response is logged at the scope's end, or in zero-change mode at the start of the next `Consume` (before it blocks), `Commit`, `StoreOffset`, `Close` or `Dispose`; the handler's calls lie between; `durationMs` is the processing time | logged at once | S9 |
| T42 | R2: the consume arrow's internal-flow segment holds the handler's spans and not the producer's; Playwright: the popup lists them | empty | S9 |
| T43 | R2: switch off (default) and no scope used, the report is byte-identical to R1's, internal-flow data included (guard) | passes | S9 |
| T62 | R2, zero-change mode: `await Task.Run(() => consumer.Consume(ct))` (documented: nothing current for the handler), a loop that wraps each `Consume` in its own `Activity`, and two consumers in one loop: after each `Consume` and after each scope closes, `Activity.Current` is what the caller had, never a stopped span (4.5) | the first draft's implicit mode parents on stopped spans, RUN P3 | S9 |
| T63 | R2: the deferred response carries the identity, phase and ids captured with the request, though F10 has cleared the message identity by then (4.5) | the tracker re-resolves at the response | S9 |
| T64 | R2: the produce's `{topic} publish` span exists and its `traceparent` names it; `Kronikol.Kafka` is a well-known source, so `AutoInstrumentation` keeps the trace | no span | S9 |

### 5.3 Mutations, each run once and caught by the fact named

| Mutation | Caught by |
|---|---|
| The prefix returns `true` (lets the closed-over body run) | T1, T2 |
| `Reset()` unpatches again | T4 |
| The prefix keys the shape on the closed pair instead of the runtime type | T3, T7 |
| Patches keyed by the closed pair instead of the code shape | T38 |
| The constructor invoked through a call that wraps its exceptions | T37 |
| Identity resolved in the delivery handler, not at the call | T11, T23 |
| Phase and verbosity read in the delivery handler | T52 |
| The request half stored at the call | T51 |
| `report.Error` ignored | T12 |
| The caller's message written to (identity headers added to it) | T16 |
| The background identity stamped | T45 |
| A synchronous `Produce` or a `Consume` that throws left unlogged | T48, T49 |
| Each line a T21 fact covers, deleted in turn | that T21 fact |
| The clear moved back after the null/EOF return | T17 |
| `Activity.Current` read inside the delivery handler | T24 |
| `traceparent` overwritten when present | T26 |
| Injection when an inner wrapper sits between Kronikol and Confluent | T54 |
| Flags always `00` | T47 |
| A consume records its ids whatever test it is recorded under | T55 |
| A consume falls back to the loop's `Activity.Current` | T56 |
| The produce takes a new Kronikol trace id | T57 |
| Parsing back to the first draft's rules (upper case lower-cased, version `01` refused) | T58 |
| The key moved back into the body, or the value wrapped (Q16 (B)) | T59 |
| The key line hidden with the headers | T60 |
| The deferred response re-resolves its identity | T63 |
| The zero-change scope stops its span without restoring `Activity.Current` | T62 |
| The shape key computed without recursing into generic value types | T38 |
| A type marked patched before its patch is installed (the shipped `TryAdd`, then patch) | T39 |
| The tiering check reads the runtimeconfig before the environment, or takes `false` for off | T9 |
| Any `InvalidOperationException` taken for a refusal, `ObjectDisposedException` included | T14 |
| A dependent producer's delivery setting read from itself, not its owner | T65 |
| Trackers registered at `Enable…` rather than at their first client | T46 |

### 5.4 The broker facts

In `Kronikol.Tests.Kafka` itself, as the real-server facts of ClickHouse and MongoDB sit in their extension's own test
project (`ClickHouseServerFixture.cs:12-57`, `MongoServerFixture.cs:12-58`, the latter added by #136 in 4.10.0), not
in a project of their own: a `KafkaBrokerFixture` on `Testcontainers.Kafka` 4.8.1 (the version the repo's five
Testcontainers references pin; there is no central package file) with its `Image` pinned, as both precedents pin
theirs (`confluentinc/cp-kafka:7.6.1` in KRaft mode is what Kronikol4J's tests run). An env var names a broker, else
Testcontainers; it skips with the reason only on `DockerUnavailableException`, as the ClickHouse plan justified under
the owner's skip rule (`CLICKHOUSE_DRIVER_CLIENT_PLAN.md:214-216`). Facts: a produce, a dependent producer's produce
and a consume through real clients, one trace across them, the content queryable, and a produce to a stopped broker
drawn as failed. CI's remainder lane runs the project on ubuntu-latest, which has Docker (`ci.yml:287-302`; the
ClickHouse facts ran there, `CLICKHOUSE_DRIVER_CLIENT_PLAN.md:400-402`); the release checklist reads that job's log to
see the broker facts ran rather than skipped.

## 6. Slices, releases and records

### 6.1 Slices

| Slice | Contents | Release |
|---|---|---|
| S0 | Research and probes (done: harness `s0/`, `tiermit/`, `prototype/`; the audit's re-runs and probes in `audit/`) | |
| S1 | Dispatching prefix keyed by code shape, construction without wrapped exceptions, patching under a lock, never unpatch, constructor lookup, tiering check and diagnostic (core registry + IVT), one tracker per enabled pair, the test project's setting and net8.0 target; T1 to T10, T37 to T39, T46 | R0 |
| S2 | Call-time capture (identity, phase, verbosity, timestamp) with both halves stored at delivery, the caller's message never written, the background identity not sent (Q18), failures on every path including a cancelled wait, delivery reports off read from the configuration (the owner's for a dependent producer), timestamps, identity at each poll, labels, phase content, coverage; T11 to T19, T21, T45, T48 to T52, T65; the existing facts that read the caller's message, rewritten | R0 |
| S3 | XML docs (with the 4.11.0 sentences F4 falsifies and `QueryCommand.Trace.cs:6`), the R0 wiki edits (6.4), the R0 Kronikol4J ledger line; T20 | R0 |
| S4 | `DependentProducerBuilder` patch; T22, T23 | R1 |
| S5 | W3C ids by the rules of 4.3, `traceparent` where Kronikol's wrapper is the last, `PropagateTraceContext`, parsing by the runtime's rules, the Kronikol trace id (Q19), listener (IVT); T24 to T26, T28, T30, T31, T47, T54 to T58 | R1 |
| S6 | Message content per Q16 and the hand-written path per Q17 (`TrackProduce`, `TrackConsume`, the wrappers built on them); T59 to T61, or T32 to T36 under (A) | R1 |
| S7 | Broker facts (5.4); R1 docs, Kronikol4J ledger line | R1 |
| S8 | Consumer acceptance: BreakfastProvider's docker lane in a scratch clone on local packages (its `KafkaEventPublisher` starts a producer `Activity`, `KafkaEventPublisher.cs:62`, so its produce entries must carry that span's ids; it is on 4.9.1 since `0ee43e8`), with a control on the release published before R1 that must lack them | R1 |
| S9 | R2 (4.5), only if Q6 says so; T40 to T43, T62 to T64 | R2 |

### 6.2 Releases

| Release | Contents | Bump | Why this part moves |
|---|---|---|---|
| R0 | S1 to S3 | patch | Bug fixes and corrected docs; nothing new for a consumer to call (`DiagnosticKind.CaptureDegraded` already exists, and the registry it needs is internal). Behaviour that changes is called out: `Reset()` no longer unpatches, produce durations are measured, failed produces and consumes are drawn, the Raw labels lose their trailing space, the caller's `Message` is no longer written (a test that read Kronikol's headers off its own message reads them off the message delivered), and, if Q18 says so, the background identity is no longer sent |
| R1 | S4 to S8 | minor | New public surface: `PropagateTraceContext`, `KafkaTracker.TrackProduce`/`TrackConsume` (Q17), a third message header constant (Q19), and a builder the package did not cover. Under Q16 (B) the Kafka note's content changes shape, a change to report output that CLAUDE.md does not count as major on its own; the changelog names it. CLAUDE.md: "anything new is a minor" |
| R2 | S9 | minor | A new option, and `TrackConsume`'s span |

Version numbers are taken when each release is cut; until then this plan says R0, R1, R2.

### 6.3 Changelog drafts

> **Patch: Kafka build interception returns the right client for every type pair, and every Kafka call is drawn as
> it happened (#135; plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md R0).** Enabling tracking for one pair made every
> other pair that shares its code in the process (another reference-type pair for `<string,string>`, another
> `<int,…>` pair for `<int,string>`) return a client of the enabled pair, which threw `EntryPointNotFoundException` on
> first use, and `Reset()` did not repair it. One dispatching patch per code shape now builds each builder's own
> client, throws what `Build()` throws, and wraps the client only when its pair is enabled; the patch stays for the
> process (`Reset()` and `Disable…` clear tracking only); hosts that enable tracking at the same time no longer lose
> their first build to a race. Under tiered compilation the runtime can replace the patched
> method after the first few dozen builds, after which nothing is tracked: the interceptor now says so on standard
> error and in the run's diagnostics, and the wiki gives the one-line remedy. `Produce(…, handler)` is attributed to
> its caller, in its caller's phase, not to the test that built the producer; a failed delivery, a `ProduceAsync` or
> `Produce` that throws and a `Consume` that throws are drawn with their error, and a `ProduceAsync` whose wait was
> cancelled says the message may still be delivered; a producer with delivery reports off no longer throws on a plain
> `Produce`, and a dependent producer whose owner has them off is logged instead of silently missed; a produce's
> duration is its broker round trip and other Kafka entries no longer carry an invented 0 ms one; the caller's
> `Message` is no longer written to (Confluent is handed a copy carrying the identity headers, each
> only if the caller's lacks it); [if Q18: a produce made outside any scenario no longer stamps the background
> identity;] a message's identity ends at the next poll; enabling a pair again no longer registers another tracker;
> the Raw label of an operation without a topic loses its trailing space. The patch part moved because nothing is
> new for a consumer to call.

> **Minor: Kafka entries carry W3C trace ids, `DependentProducerBuilder` is intercepted, a message's value is
> recorded as its body, and a decorator of any Kafka client can track it (#135, #160, #167;
> plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md R1, roadmap 1.21).** `EnableProducerTracking<K,V>` now also tracks
> producers built with `new DependentProducerBuilder<K,V>(handle).Build()`, the way wrapper libraries share one client
> between producers. A produce entry records the W3C trace and span id of the message's `traceparent` or of the
> caller's `Activity`, taken at the call; a consume entry those of the consumed message's `traceparent`, when it is
> recorded under the test the message names; a hand-written `LogProduce`/`LogConsume` those of `Activity.Current`. A
> produced message with no `traceparent` gets one when Kronikol's wrapper is the last before Confluent
> (`PropagateTraceContext`, default true), so a consume joins its producer's trace; [if Q19: a message also carries
> the Kronikol trace id of the request it was produced in, so `kronikol query flow` nests its delivery under that
> call.]
> A Kafka note now shows the message's value as its body, indented and queryable like an HTTP body
> (`kronikol query values <report> --request --path '$.Status'`), with the key above it and the message's headers as
> the entry's headers. [if Q17: `KafkaTracker.TrackProduce` and `TrackConsume` give a decorator of any Kafka client
> the wrappers' behaviour.] Report output changes: Kafka notes change shape, Kafka entries gain `activityTraceId` and
> `activitySpanId`, so `kronikol query trace` includes them and the whole-test flow gains the handler's spans for a
> consumer that parents on the message, and exported Kafka spans move into the app's trace. The minor part moved
> because `PropagateTraceContext`, `TrackProduce`, `TrackConsume` and a message header constant are new public surface
> and a builder the package did not cover is now covered. (Under Q16 (A), the content sentences become the first
> draft's `ContentFormat` ones; without Q17 or Q19, their sentences and their names in this list drop out.)

### 6.4 Wiki

The local wiki checkout is 26 commits behind origin (at `e164dbc`, 4.5.0; origin `8a57576`, 4.14.6) and other
sessions keep wiki worktrees of their own: edit in a new one (`git worktree add ../Kronikol.wiki-135 -b wiki-135
origin/master`), apply after publication from `wiki/r0_wiki.py` and `wiki/r1_wiki.py`. Lines below are at `8a57576`.

| Page | Change | Release |
|---|---|---|
| `Integration-Kafka-Extension.md` `:448-530` | Interception: how the dispatching patch works, that it stays for the process (`:452`, `:505-516`: `Reset()` no longer "removes all Harmony patches"), pairs are independent now (`:528`), the tier-up note and remedy with its measured cost | R0 |
| `Integration-Kafka-Extension.md` `:446` | The choosing table's Option G, "zero-change tracking", now needs `<TieredCompilationQuickJit>false</TieredCompilationQuickJit>` in the test project to stay reliable | R0 |
| `Integration-Kafka-Extension.md` `:41-48`, `:57`, `:179`, `:195` | The Raw labels of every operation without a topic (Unsubscribe, Commit, Flush and the five transaction rows, not only `Commit`); the `Produce` row after F7 (logged at the call, without partition or offset, when delivery reports are off); the manual DI example's options (F15); `CurrentStepTypeFetcher` "has no effect", as its source says since 4.12.1 | R0 |
| `Integration-Kafka-Extension.md` `:557`, `:593-602` | The identity headers: only an identity that names a scenario travels, and never written into the caller's `Message` (4.2) | R0 |
| `API-Reference.md:245-258` | The seven missing options (`HttpContextAccessor`, #136's, was never added), the transaction members, the two factory registrations, rows for the interceptors; `:253`'s "auto-resolve `IHttpContextAccessor` from DI" (the option's accessor wins since 4.11.0) (F15) | R0 |
| `Background-Thread-Correlation.md:57-62` | A message's identity ends at the next poll, whatever it returns (F10), not only when the next message arrives | R0 |
| `Generated-Reports.md:298`, `:299`; `Exporting-to-OpenTelemetry.md:32` | The bang status: the exception's type, or for Kafka the error code (4.2); `durationMs` absent when both halves carry one instant, the rule R0 puts Kafka's one-instant entries under | R0 |
| `Querying-Reports.md:842-862` (claim `:844`), `Integration-OpenTelemetry-Extension.md:7-9`, and `QueryCommand.Trace.cs:6` in the source | Name the trackers that record W3C ids instead of "every interaction" (F16), including deferred logging (`PendingLogEntry.ActivityTraceId`) and the proxy's deferred mode; Kafka joins the list in R1 | R0, R1 |
| `Integration-Kafka-Extension.md` `:3`, new sections | The "shared library or abstraction layer?" callout (`:3`) gains `DependentProducerBuilder` and the per-pair, process-wide limit (#160); new sections "Dependent producers", "Trace ids and `traceparent`" (on the pattern of `Integration-Grpc-Extension.md:457-468`, rewritten by 4.12.1), and "Message content" for Q16's answer, with its `values` recipe; `:27-29` and `:247-255` content formats | R1 |
| `Integration-Kafka-Extension.md` (new section "Decorating your own Kafka client") and `API-Reference.md:249` | `KafkaTracker.LogProduce`, `LogConsume` and the other public `Log…` methods (`KafkaTracker.cs:31-91`) are documented nowhere; the reporter's path and Q17's `TrackProduce`/`TrackConsume` (with R2's scope) need a home, with #160's decorator as the worked example | R1, R2 |
| `Internal-Flow-Tracking.md:15`, `:17`, `:31`, `:115-132`, `:336` | Kafka records ids (R1); the listener is started by the Kafka trackers as well as the HTTP handler (`:31`, `:336`, F14); what `activitySpanId` means on a consume (the producer's span, `:17`); `Kronikol.Kafka` among the well-known sources (R2) | R1, R2 |
| `Generated-Reports.md:295`, `Ingesting-External-Captures.md:34` | The same sentence on a consume's `activitySpanId` | R1 |
| `Querying-Reports.md:857-858` (and the skill's `SKILL.md:116`) | Which Kafka ids can repeat across scenarios (4.6), so `trace`'s leak warning is read rightly | R1 |
| `Exporting-to-OpenTelemetry.md:64-67` | Kafka spans now follow the captured trace | R1 |
| `Filtering-and-Redacting-Diagram-Content.md:20` | A Kafka body cut at `MaxContentLength` no longer parses as JSON (no YAML view, no `values`), as for an HTTP body | R1 |
| `README.md:279` (repo) | The Build Interception row names `DependentProducerBuilder` too | R1 |
| `src/Kronikol/Reports/agent-instructions.md:107` (written beside every report), `.claude/skills/kronikol-test-debugging/SKILL.md:108`, `references/commands.md:190-210`, and the `templates/skills/` copies | The `values` recipe for a message payload (`--request`, or Q11's answer); `SkillDriftTests` pins only verbs and flags, so nothing else catches it | R1 |
| `Multi-Host-Test-Architectures.md:12-14`, `Event-Driven-Architecture-Testing.md:14-37` (optional) | A trace across Kafka between hosts; #160's limit | R1 |
| `Event-Driven-Architecture-Testing.md:101`, `Parallel-Safe-Background-Correlation.md:168` | `AddKafkaTracking` does not exist. #142 item 3 lists only the first; R0's wiki edit takes both, and #142's executor is told | R0 |

### 6.5 Kronikol4J

The golden parity tests pin old .NET output (`GoldenHtmlParityTest.java:63` and `ReportDataParityTest.java:36`, 3.0.43
HTML and 3.0.47 data), but one Java fact pins a line R0 changes: `KafkaOperationClassifierTest.java:24-26` expects
`"Flush "`, "trailing space after the operation, matching .NET" (`KafkaOperationClassifier.java:28`). One ledger line
per release in `docs/REMAINING_PARITY.md` after publication, placed before `## Explicitly OUT OF SCOPE` (`:2432`; the
ledger ends at `:2428`), in S3 for R0 and S7 for R1:

- **R0, worth porting:** the trimmed Raw label (F11, and that Java fact turns round); a failed send drawn as a failure
  (the port logs `Sent` before it calls `send`, `TrackingKafkaProducer.java:33-51`, `MessageTracker.java:134-155`: F5
  and F6's gap). **Nothing to mirror:** Harmony interception (no Java form, `REMAINING_PARITY.md:1769-1790`); F4 and
  F10 (the port logs at send time and scopes a consumed identity to the log call, `TrackingKafkaConsumer.java:86-95`);
  F8 (its messaging entries carry no timestamp at all); F9 (`KafkaTestHeaders.stamp` already removes before adding,
  `:22-28`, pinned by `TrackingKafkaProducerTest.java:43-46`).
- **R1, not mirrored:** ids (the port records none on a messaging entry; `OtelBridge.stamp` has no caller in main
  code). The content shape follows Q16: the port does not log "the value alone as JSON" as this plan once said; it
  writes a `String` value as a quoted JSON string and a `byte[]` as an array of numbers
  (`TrackingSafeSerializer.writeValue`), so a JSON payload is no more queryable there than in .NET today.
- The open "Kafka — DECISION NEEDED" note (`:46-61`) recommends moving Java to .NET's `Key: …, Value: …`. If Q16 takes
  #167's shape, that recommendation reverses, and R1's ledger line says so.

### 6.6 Before declaring done

Every fact in sections 5.1 and 5.2 found in the diff; each red run kept against the release published before each
release (`r0/red-<previous>-*.txt`, `r1/red-<R0>-*.txt`, R1's facts on R0's tree with compile stubs for the new
options); `mutations.txt`; `release.slnf` built in Release for every target; the build-interception facts run on
net8.0 as well as net10.0 (F2 depends on the runtime and the reporter runs .NET 8: the test project multi-targets,
as `Kronikol.Tests.AssertionTracking.csproj:5` does, and CI installs 8, 9 and 10); the harness's `s0/` and `tiermit/`
re-run on the Confluent.Kafka and Lib.Harmony versions a fresh restore resolves (2.16.0 and 2.4.3 on 2026-10-12, both
floating as `2.*` in the package csprojs); new test classes that touch process-wide state join the existing
collections (`TestCorrelationStore`, `TrackingComponentRegistry`; `ProcessWideTrackingStateTests` guards it); the
broker facts seen running in CI's remainder job log; the full Playwright suite for R1, its Kafka facts built on
entries in Kafka's shape (the E2E project references Kronikol, Otlp and Grpc, not Kafka) or on a new reference; every
doc comment on the touched types re-read by grep; #135 closed with a comment naming the releases; leftovers in
`ROADMAP.md` Appendix C, and Appendix C's open row for the gRPC plan's Q3 (`ROADMAP.md:955`) updated for Kafka's
part (4.2).

## 7. Where it sits in the roadmap

Stage 1 row **1.21**, with decision **D37** for the green light and Q1 to Q19, both placed on 2026-10-12 when the
owner green-lit the plan in full. Both were reserved on 2026-10-07 by message with the sessions planning #132, #134,
#136 and #141, none of which takes a row before its plan is green-lit. Since then rows 1.22 (#136), 1.23 (#141), 1.24 with
D38 (#132), 1.25 (#162) and 1.27 (#163, its R1 released as 4.14.8) were placed, and 1.26 is PR #164's (4.15.0, in
review); there is no D39. At 4.14.9, 1.21 and D37 were still free (`ROADMAP.md:252-253`, `:179-181`), D38 sat
directly after D36, so D37's row went between them, and other committed plans record 1.21 and D37 as #135's
(`GRPC_IDENTITY_PROPAGATION_PLAN.md:766`, `MONGODB_ACCESSOR_OPTION_PLAN.md:334`, among others). The coverage check's
issue list (`ROADMAP.md:641`) gained `#135 → 1.21, D37`, #160 (its helpers; its per-host options stay open) and #167.

## 8. Found on the way, not in this plan

Each is another subsystem's, so it is recorded here rather than folded into these releases (Q12):

- **MassTransit** clears the message identity *before* logging the consume (`TrackingConsumeObserver.cs:24-31`, READ),
  and stamps each half of a pair separately as Kafka does (`MassTransitTracker.cs:105-137`, READ). No tests cover the
  observer.
- **ServiceBus, EventHubs and PubSub** log a failed send as response content with no status, so
  `InteractionStatus.IsError` reads it as a success (`TrackingServiceBusSender.cs:44-47`,
  `TrackingEventHubProducerClient.cs:57-60`, `TrackingPublisherClient.cs:49-52`, READ).
- **Most trackers record no W3C ids** (MongoDB, SQL, Redis, Cosmos, ServiceBus, and the DispatchProxy and MediatR
  proxy in their immediate log mode, `research-kronikol.md` §5): the same gap as ask 2, everywhere but HTTP, gRPC,
  `MessageTracker`, the taps and the proxy's deferred mode (`TrackingProxy.cs:101-108`).
- **#160** (filed 2026-10-08 by the same reporter): a company library builds producers with
  `DependentProducerBuilder` and consumers with `ConsumerBuilder` behind its own interfaces, so its suite decorates
  those interfaces per host in about 200 lines, copying what the wrappers keep private (stamping the identity headers,
  taking the identity from a consumed message, the content text). It asks for public helpers
  (`KafkaTracker.TrackProduce(topic, partition, message)`, `TrackConsume(result)` returning the identity scope) and for
  interception options per builder or per host. Ask 1 is half of its first sentence; the rest is new surface on the
  same types this plan's Q8 overloads add to (Q17).
- **#167** (filed 2026-10-11 on 4.14.6): the `Key: …, Value: {…}` line is neither indented, nor offered the YAML view,
  nor queryable; it asks for the value as the body, the key and the message's own headers as the entry's headers
  (with the key shown above the body whatever the header toggle says), and `LogProduce`/`LogConsume` overloads taking
  them. That is ask 3 in another shape: shipping section 4.4's envelope first would give Kafka two content formats and
  an option #167's shape makes redundant (Q16).
- **#134** (gRPC identity propagation) shipped as 4.12.1, 4.13.0, 4.13.2 and 4.14.3 after this plan was written, and
  set conventions R0 and R1 follow (sections 4.2 and 4.3): the caller's object is never written to (4.12.1 copies a
  gRPC call's `Metadata`), only an identity that names a scenario travels (4.13.0), and a `traceparent`'s flags say
  `01` when the span is recorded (4.12.1, 4.14.3). **#142** (docs) is still open. **#136** shipped in 4.11.0
  (`709b3afc`, 2026-10-08): it added `KafkaTrackingOptions.HttpContextAccessor` as the options' last member, an internal
  `KafkaTracker.HttpContextAccessor` and a constructor fallback to the option, makes the four registrations in
  `KafkaServiceCollectionExtensions.cs` prefer it to the container's, passes the tracker's accessor in
  `InjectTestIdentityHeaders`, and adds one row to `Integration-Kafka-Extension.md`'s options table; the two wiki
  samples that set the property (`HTTP-Tracking-Setup.md:228-245`, `Diagnostics-and-Debugging.md:410-419`) then
  compile. This plan builds on it: R1's new options go after that member.
- **The receiving side accepts the background identity.** `TestIdentityScope.SetFromMessage` and the HTTP middleware's
  `Begin` (`TestTrackingContextMiddleware.cs:33`) adopt `Unknown`/`unknown` as a scenario's, which is how F20 turns a
  background produce into an attributed consume. Q18 stops Kafka sending it; refusing it on receipt is core's, and
  would cover the HTTP handler, which still sends it (`TestTrackingMessageHandler.cs:214-230`), and the other messaging
  producers (READ, audit `C-review.md` H5).
- **ProxyTap's public `TraceParent.TryParse`** (`ProxyTapOptions.cs:183-195`) accepts what the runtime refuses: upper
  case, version `ff`, a zero span id and trailing text; ProxyTap also forwards flags `01` whether or not its span is
  recorded (`ProxyTap.cs:247`, `ProxyTapOptions.cs:198`) (RUN, audit P5). Kafka parses with `ActivityContext.TryParse`
  (4.3) rather than reuse it. PR #164 (4.15.0, in review) records the parser as its plan's F4 and asks in its Q2 for a
  W3C patch that would still read upper case (`PROXY_TAP_TRACE_CONTEXT_PLAN.md` on its branch), and it moves both
  files' lines.
- **Nothing checks the wiki's API pages against the public API.** F15's drift (seven Kafka options missing, one since
  4.11.0) will recur on every page `API-Reference.md` summarises; only `SkillDriftTests` guards a doc (CLI verbs and
  flags).
- **#97 / D21** (fixed floors for the extensions' client dependencies, open): this plan's early warning of a Confluent
  or Harmony change relies on the floating `2.*` (4.1, A1, A2).
- **Upstream:** Harmony loses detours on shared generic code after tier-up (F2). Worth a report to Harmony with
  `tiermit/` as the repro; still true of Lib.Harmony 2.4.3 (RUN, audit `B/`).
- **Every pack raises the consumers' floors (#97).** The `2.*` floats made 4.10.0 and later require Confluent.Kafka
  2.16.0 (4.9.0 and 4.9.1 declared 2.15.1); a suite pinned to 2.15.1, as #135's reporter's is, gets NU1605 on restore
  and, forced, a `FileNotFoundException` for Confluent.Kafka 2.16.0.0 at the first call (RUN, audit `B/` section 0).
  The next pack of the build-interception package will likewise require Lib.Harmony 2.4.3. Whichever release of any
  plan is packed next carries that; the changelog of the first one should say so.

## 9. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Tier-up: is a diagnostic and a documented remedy enough, or should the build-interception package set `TieredCompilationQuickJit=false` for its consumers (a `buildTransitive` props, overridable)? | **Diagnostic and docs in R0; decide the props for R1 after measuring it on BreakfastProvider.** It makes interception reliable by default, at a startup cost measured at +0.6 s on the 126 facts the Kafka suite had at 4.9.0, in a package only test projects reference. It works: a probe package's props reached the runtimeconfig of a test project that references it directly and of one that reaches it through a project reference, xunit.v3's test host and VSTest's `testhost` both obeyed it (2,000 of 2,000), and a project's own setting overrides it (RUN, audit `B/` E5). An environment variable still overrides it, so the diagnostic stays either way. `Kronikol.AssertionTracking.csproj:29-30` already packs a `build/` props as `buildTransitive` |
| Q2 | Also count clients that escaped (a non-generic watchdog on Confluent's internal `SafeKafkaHandle.Create`, stable 2.0.2 to 2.15.1, which survives tier-up)? | **No.** It adds a second internal dependency and cannot see dependent producers; the settings check names the cause before anything escapes |
| Q3 | When Confluent's internal constructors are missing: throw at `Enable…`, or warn and track nothing? | **Throw.** A silent loss is what F1 and F2 were |
| Q4 | Ask 1 in R0 (no new public surface) or R1? | **R1.** It covers a builder the package never promised to cover; R0 stays a pure patch. Both can ship the same day |
| Q5 | Add `traceparent` to produced messages by default? | **Yes, under a new `PropagateTraceContext` defaulting to true,** and only where Kronikol's wrapper is the last before Confluent (4.3). There is a precedent for the bump: 4.13.0 shipped `GrpcTrackingOptions.PropagateTestIdentity`, default true, as a minor, its changelog saying "no existing option's default changes" (ROADMAP rule 7, `ROADMAP.md:90-93`). A message that has one is never touched |
| Q6 | The arrow popup: (a) document that ids fix `trace`, and the whole-test flow for a parenting consumer, only; (b) R2's processing span (4.5), as a scope and an opt-in zero-change mode; (c) a builder rule: a call whose own span is stored takes that span's subtree, whatever its time | **(b), as R2,** with the scope as its documented form. It is what the reporter expected and the only option that also gives a consume a real duration; the zero-change mode fails in shapes the audit measured (4.5), which its facts pin and the wiki names. (c) cannot tell a call's own span from its caller's for HTTP calls |
| Q7 | `KafkaTracker.BeginConsume` for hand-written logs, in R2? | **Folded into Q17:** `TrackConsume` returns the scope, and R2 gives it its span |
| Q8 | Overloads of `LogProduce`/`LogConsume` that take the message's `Headers`, so a hand-written log reads `traceparent` and identity? | **Superseded by Q17.** Three asks would add overlapping hand-written-log APIs (these overloads, #167's overloads taking key and headers, #160's helpers); Q17 is one. Whatever ships takes overloads, not optional parameters (a binary break) |
| Q9 | Headers in the content? | **Follows Q16.** Under (B) the message's own headers are the entry's headers, read before Kronikol adds its own; under (A) none in the envelope |
| Q10 | Protobuf through reflection on `Google.Protobuf.JsonFormatter`; Avro? | **Protobuf yes; Avro out** (its records do not serialise to useful JSON without the schema; a user can log a string) |
| Q11 | Should `values` read the request half of an event call by default? | **No; document `--request`,** in the wiki and in the agent files written beside every report (6.4). A per-call default is a surprise elsewhere |
| Q12 | The sibling defects in section 8: here or separately? | **Separately,** as their own patch with red facts |
| Q13 | `KafkaTrackingOptions.HttpContextAccessor` (the wiki samples) | **Settled:** #136 shipped it in 4.11.0; R0 keeps its argument (4.2), and `API-Reference.md` gains the row it never got (F15) |
| Q14 | Mirror ids or the content shape in Kronikol4J? | **No, ledger only,** until its "Kafka — DECISION NEEDED" note is taken. If Q16 takes (B), the note's recommended direction reverses, and the R1 ledger line says so |
| Q15 | Real-broker facts? | **Yes** (5.4), in `Kronikol.Tests.Kafka` with a pinned image, as ClickHouse's and MongoDB's sit in their own extension's test project |
| Q16 | Ask 3's shape: (A) the first draft's opt-in `ContentFormat` with a `{"key","value"}` envelope, or (B) #167's: the value as the body, the key and the message's headers as the entry's headers, the key drawn above the body whatever the header toggle says, and no option? | **(B), in R1.** It serves #135's ask (a payload `query values --path` reaches) and #167 with one shape, keeps `$.Status` as the reporter wrote it, ships no option that a later move to (B) would have to flip (a major), and today's readers already read it (4.4). The cost is a page and emitter change for the key line, with Playwright facts |
| Q17 | The hand-written path: Q8's overloads, or #160's public helpers, `KafkaTracker.TrackProduce` (returns the message to send, a copy carrying the identity, the trace id and `traceparent`, and a completion for delivered or failed) and `TrackConsume` (logs, makes the message's identity current, returns an `IDisposable` scope that R2 gives its span)? | **#160's helpers, in R1, with the wrappers built on them,** so a hand-written decorator (the reporter's for #135, the library of #160) gets every R0 fix and the R1 ids, where R0 confines them to an internal overload. #160's second ask, interception options per host, stays out of this plan (4.6) |
| Q18 | Stop stamping the background identity on Kafka messages (F20), as gRPC stopped in 4.13.0? | **Yes, in R0,** which rewrites that method. The receiving host resolves the same background id with its true source. The HTTP handler and the other messaging producers stay with the gRPC plan's open Q3 (`ROADMAP.md:955`), and `SetFromMessage`/`Begin` accepting the background id on the receiving side is core's (section 8) |
| Q19 | Carry the Kronikol trace id on Kafka messages (F21): the produce takes the request's, the message carries it, the consume takes it? | **Yes, in R1, under `PropagateTestIdentity`,** as HTTP and gRPC carry it from hop to hop. It is what makes `flow` nest a delivery under the call that published it (RUN, S3), the Kronikol half of ask 2's "follow a call into Kafka and out of the consumer" |

## 10. Assumption ledger

| # | Assumption | Why it is safe, or what checks it |
|---|---|---|
| A1 | Confluent's internal constructors stay as they are within 2.x, and `Build()` stays `new Producer<K,V>(this)` | Same signatures in 2.0.2, 2.14.0, 2.15.1, 2.16.0-RC1 and the released 2.16.0 (reflection, 2026-10-12); each of the three `Build()` methods is the same 7 bytes of IL (`ldarg.0`, `newobj`, `ret`) in 2.14.0, 2.15.1 and 2.16.0 (RUN, audit `B/`), so skipping it loses nothing; the package's floating `2.*` makes CI build against the newest; T10 pins the refusal |
| A2 | Harmony 2.4.x keeps its tier-up behaviour; quick JIT off keeps the detour | Measured on 2.4.2 and 2.4.3, .NET 8 and 10 (`tiermit/`, audit `B/`); T8 runs in CI under the setting, on net8.0 and net10.0 |
| A3 | The reporter's hand-written `LogConsume` runs inside their processing span | ISSUE (their description); Q17's `TrackConsume` covers the case where it does not |
| A4 | The quick-JIT cost scales below the 37% measured on a small suite | One suite measured; Q1 measures BreakfastProvider before any default changes |
| A5 | R2's zero-change mode: a consumer's next `Consume` marks the previous message done | The sequential loop every documented pattern uses; the audit measured where it fails (4.5, RUN P3), and the scope form does not depend on it |
| A6 | An instrumentation wrapper injects its `traceparent` before its first await, so Kronikol can read it from the copy when the inner call returns | READ in OpenTelemetry's contrib package (`InstrumentedProducer.cs:382-386`) and `Confluent.Kafka.Extensions.Diagnostics` (`ActivityDiagnosticsHelper.cs:31-43`); T54 pins it with a wrapper of that shape |

## 11. Log

- **2026-10-07.** Written at 4.9.0 (`417c8e58`) in worktree `C:/Code/Kronikol-kafka135`, the day #135 was filed.
  Read the Kafka extension, its build interception, its tests, `KafkaTracker`'s readers (internal flow, `trace`,
  `values`) and Confluent.Kafka 2.0.2, 2.15.1 and 2.16.0-RC1's source. Ran the probes in the harness on .NET 8.0.27
  and 10.0.11: the shipped package's F1 (re-run here: `Produce` threw `EntryPointNotFoundException`) and F2 (151
  builds), the attribution, delivery-reports and timing defects, the dispatching prototype and the tier-up
  mitigations. Baseline: `Kronikol.Tests.Kafka` 126 of 126 and `Kronikol.Tests.Kafka.BuildInterception` 17 of 17
  pass at `417c8e58`. Row 1.21 and D37 reserved with the sessions planning #132, #134, #136 and #141; #134's plan
  shares the `traceparent` format (flags from the `Activity`). Nothing committed.
- **2026-10-08.** Other sessions are executing #136 (row 1.22), #141 with #144 and #145 (row 1.23) and #132 with #123
  (row 1.24, D38), each off `e59305da`. #136's R2 edits `KafkaTrackingOptions.cs` (a last member),
  `KafkaTracker.cs`'s constructor and `TrackingKafkaProducer.cs:108` (section 8); this plan's edit sites are below
  those lines or new members, and whichever release of this plan comes first rebases onto it. Version numbers here
  stay R0, R1, R2 until cut.
- **2026-10-08, later.** 4.11.0 is on origin (`709b3afc`, tag `v4.11.0`) with #136's Kafka changes as section 8
  describes. The worktree moved onto it (`plan/kafka-135` at `709b3afc`, LF) and the `PLANS_STATUS.md` row went back on
  top of the current table. Between `417c8e58` and `709b3afc`, of the files this plan cites only `KafkaTracker.cs`
  (three lines after `:25`), `TrackingKafkaProducer.cs` (one line at `:108`), `KafkaTrackingOptions.cs` (a last member)
  and `KafkaServiceCollectionExtensions.cs` changed: the build interception, core's internal flow, `query trace` and
  `Kronikol.csproj` did not (empty diffs). Every finding stands; Appendix A gives the shifted lines.
- **2026-10-12, audit.** At the owner's "make sure nothing was missed from the plan, that there isn't any bugs or
  issues in the implementation", run in a worktree of its own (`C:/Code/Kronikol-audit135`, branch `audit-135` at
  `d59843fa`, 4.14.6, LF), with four reviews whose notes are in the harness's `audit/` folder: every citation and claim
  re-checked at 4.14.6 (`A1-citations.md`), the wiki, Kronikol4J, roadmap and CI claims (`A2-docs.md`), the probes
  re-run on the published 4.14.6 packages with the experiments of section 4.1 (`B/`), and an adversarial review of
  sections 4.2 to 4.6 and 5 with a probe on 4.14.6 (`C/`). Nothing in this plan was implemented by anyone; 30 commits
  had landed between `709b3afc` and `d59843fa` (4.11.1 to 4.14.6), none in the Kafka extension but one doc comment,
  and 5 more by the audit's end (4.14.7 to 4.14.9, `5b2c3a32`), touching no file this plan cites but `ROADMAP.md`,
  whose lines it now gives at `5b2c3a32`. PR #164 (4.15.0, in review) changes ProxyTap, which section 8 cites. What
  changed here:
  - **Missed issues.** #160 and #167, filed by the same reporter after the plan, overlap ask 1, ask 3 and Q8 (section
    8, Q16, Q17).
  - **Missed findings.** F18 (shared code by value-type argument), F19 (a tracker per `Enable…`), F20 (the background
    identity on messages, already open as the gRPC plan's Q3), F21 (no Kronikol trace id on messages, so `flow`
    cannot nest a delivery), F22 (a synchronous `Produce` that throws and a failed consume, drawn nowhere), F23 (hosts
    that enable at once lose their first build: 9 of 10), F24 (a dependent producer whose owner reports no deliveries
    logs nothing).
  - **Design defects corrected.** Patches keyed by pair, not code shape (4.1); the prototype's reflection call wrapping
    Confluent's exceptions (4.1); the F9 fix writing into the caller's `Message`, against the rule 4.12.1 set for
    gRPC, which in R1 would have recorded a reused message's first trace on every later produce (4.2); `!{ErrorCode}`
    stated against `FailedSend`'s convention without saying so, and no rule for a cancelled wait (4.2); a produce's
    request stored at the call, which loses Raw's partition and lets `flow` nest unrelated consumes (4.2); a consume's
    ids taken from the polling loop's `Activity.Current`, and recorded whatever test the consume is recorded under,
    which strips the producing test's spans (4.3, RUN); `traceparent` injected beneath an instrumentation wrapper,
    which re-parents `Confluent.Kafka.Extensions.Diagnostics`'s consumer (4.3); a parser of its own that lower-cased
    what the runtime refuses (4.3); C4's "ids alone fix the whole-test flow" (C4); the envelope's "date as a JSON
    primitive" (4.4); R2's implicit span, which fails under `Task.Run`, a wrapping loop or two consumers (4.5).
  - **Facts.** T6, T7, T9, T13 to T16, T18, T24 to T26, T28, T31 and T40 to T43 rewritten; T27, T29 and the
    lower-casing mutations dropped (they pinned the defects above); T37 to T39, T45 to T52 and T54 to T65 added (T44
    and T53 are not used); five existing
    facts that read the caller's message listed for rewriting (section 5).
  - **Stale records.** Citations moved to 4.14.6 (Appendix A); the Kafka suite has 139 facts, not 126; `Kronikol.csproj`'s
    `InternalsVisibleTo` list and the HTTP and gRPC `traceparent` code changed (F14, 4.3); #134 shipped (section 8);
    the wiki is at `8a57576` and its lines moved (6.4); Kronikol4J pins F11's trailing space and shares F5's gap
    (6.5); row 1.25 and 1.26 were placed and 1.21 and D37 are still free (section 7); the broker facts go in the
    existing test project with a pinned image (5.4); Q5 has a precedent (4.13.0); a fresh restore now resolves
    Confluent.Kafka 2.16.0 and Lib.Harmony 2.4.3, on which every finding re-run reproduces (C1, C2, C6, F1, F2, F4 to
    F7, on net8.0 and net10.0) and the prototype holds; F2's window is shorter than the plan said (about 40 builds
    with pauses), and the tiering check, the F7 design and the patch guard were rewritten from the audit's
    measurements (4.1, 4.2; F23, F24 added).
  The status is unchanged: proposed, not green-lit, uncommitted.
- **2026-10-12, green light.** The owner: "Implement it in full, in a separate Worktree to avoid clashing with other
  sessions." Every question in section 9 is taken as recommended (D37); Q1's package default is decided for R1 after
  measuring it on BreakfastProvider, as its recommendation says. Row 1.21 and D37 placed in `ROADMAP.md`, with #160
  (its helpers) and #167 under 1.21. Executed in worktree `C:/Code/Kronikol-kafka135`, a release at a time; the old
  pre-audit worktree of that name was removed first, with the stale `Kronikol-doorstep`, to free disk.
- **2026-10-12, R0 built (S1 to S3), released as 4.16.2.** As designed in 4.1 and 4.2, with Q3, Q18 and the R0
  half of Q1 as recommended. On the way:
  - **Red first.** Every new fact was run on 4.14.10 with compile stubs for the new seams (harness `r0/red-4.14.10-*.txt`)
    and fails there on its own assertion; the guards that hold today's behaviour (T6, T8, the refusal theory, the
    delegation rows) pass there, as they should. The fact for two generic value-type keys of one shape
    (`KeyValuePair<Uri,Uri>` beside `KeyValuePair<string,string>`), added after the first red run, was run on 4.14.10 on
    its own (`r0/red-4.14.10-shape-fact.txt`): the second pair got the first pair's producer, F18 as measured.
  - **Mutations** (`r0/mutate.py`, `r0/mutations.txt`): 37, each caught. Two needed work. M3's first run hung, because
    the concurrent-enabling facts ran each host on a thread of its own and a thread's exception ends the test process
    rather than failing the fact; the helper now records each thread's exception and bounds its wait, and M3 is caught
    by 12 facts. M18c (the tiering check dropped from `EnableProducerTracking`) survived, since every fact that enabled
    under tiering enabled both directions; `Enabling_one_direction_alone_where_tiering_can_lose_the_patch_says_so` now
    catches it. The first run's process outlived its stopped shell and ran three mutations beside the second run in
    one worktree, so those three were run again alone; `mutate.py` now stops what is left running under the worktree
    after each mutation, and counts a hung or aborted run as no verdict.
  - **A guard caught a placement.** `ProcessGlobalStoreTests` failed the first full core run:
    `ProcessDiagnosticsReportTests` generates reports, which draw through the process-wide diagram cache an ingest
    resets, so it joined the `DiagramsFetcher` collection in place of a collection of its own.
  - **Docs.** `KafkaShippedDocsTests` (T20) reads the shipped XML: `Reset()` and `Disable…()` say the patch stays and the
    pair gets Confluent's own client, the classifier no longer mentions HTTP, and the accessor option's sentence holds.
    `QueryCommand.Trace.cs` names the trackers that record W3C ids (F16). The wiki edits are `wiki/r0_wiki.py`, applied
    after publication; the Raw label table needed none, since F11's fix makes the code match it.
  - **Checks.** On the tree rebased onto 4.16.1, the core suite passes 7,355 with 2 skipped after the version bump, Kronikol.Tests.Kafka passes 234, Kronikol.Tests.Kafka.BuildInterception 69 on net8.0 and 69 on net10.0, the full Playwright suite passes 1,026 with 28 skipped, and release.slnf builds in Release for every target.

## Appendix A. Edit sites at `d59843fa` (4.14.6)

| File | Lines | Change | Release |
|---|---|---|---|
| `src/Kronikol.Extensions.Kafka.BuildInterception/KafkaBuildInterceptor.cs` | 9-14, 102-153 | Dispatching prefixes, shape-keyed patches under a lock, delegates compiled per pair, constructor lookup, never unpatch, tiering check, docs | R0, R1 |
| `src/Kronikol.Extensions.Kafka.BuildInterception/*.csproj` | 6 | Description names the dependent builder | R1 |
| `src/Kronikol.Extensions.Kafka/TrackingKafkaProducer.cs` | 24-115 | Call-time capture, both halves stored at delivery, the copy with add-if-absent headers (keeping #136's accessor argument at `:109`), attributed-only stamping (Q18), failures on every path, handler refusal, ids, `traceparent`, the trace id header (Q19), docs (`:108`) | R0, R1 |
| `src/Kronikol.Extensions.Kafka/TrackingKafkaConsumer.cs` | 24-43, 70-130 | Identity at each poll, `ConsumeException`, phase content, ids by 4.3's rule, the trace id, content per Q16 | R0, R1 |
| `src/Kronikol.Extensions.Kafka/KafkaTracker.cs` | 31-196 | Internal overloads with captured context and preset timestamps, failure entries, ids, `TrackProduce`/`TrackConsume` (Q17) | R0, R1 |
| `src/Kronikol.Extensions.Kafka/KafkaTrackingInterceptor.cs` | 30-73 | One tracker per enabled pair (F19) | R0 |
| `src/Kronikol.Extensions.Kafka/KafkaOperationClassifier.cs` | 3-5, 12-15 | Summary, Raw label | R0 |
| `src/Kronikol.Extensions.Kafka/KafkaTrackingOptions.cs` | 70, new members after `:72` | The doc F4 falsifies; `PropagateTraceContext` (R1), `TraceMessageProcessing` (R2) | R0, R1, R2 |
| `src/Kronikol/Constants/HttpHeaders.cs` | 19-23 | `TestTrackingMessageHeaders`: the trace id header (Q19) | R1 |
| `src/Kronikol/Kronikol.csproj` | 13-26 | `InternalsVisibleTo` for both Kafka assemblies, in the item group | R0, R1 |
| `src/Kronikol/Reports/ReportGenerator.cs` | 472-476 | The internal registry's entries merged into the run's diagnostics | R0 |
| `src/Kronikol/Query/QueryCommand.Trace.cs` | 6 | The "every interaction" claim | R0 |
| `src/Kronikol/PlantUml/PlantUmlCreator.cs`, `src/Kronikol/Reports/collapsible-notes-script.js` | 284 (headers), 1021-1069 | The `kafka-key` line outside the header block, kept by the header toggle and skipped by the YAML view (Q16 (B)) | R1 |
| `src/Kronikol/InternalFlow/InternalFlowSpanCollector.cs` | 11-27 | `Kronikol.Kafka` | R2 |
| `tests/Kronikol.Tests.Kafka.BuildInterception/*.csproj` | | `TieredCompilationQuickJit` false; `net8.0;net10.0` | R0 |
| `tests/Kronikol.Tests.Kafka/` | `TrackingKafkaProducerTests.cs:221`, `:240`, `:255`, `:274`, `:292`, `:308-337` | Facts read the inner message; the shared fake records it; broker facts (5.4) | R0, R1 |
