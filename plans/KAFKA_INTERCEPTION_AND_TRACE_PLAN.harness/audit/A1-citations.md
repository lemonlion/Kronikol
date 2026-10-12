# A1: citation and claim audit of KAFKA_INTERCEPTION_AND_TRACE_PLAN.md at 4.14.6

Plan audited: `C:/Code/Kronikol-audit135/plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md` (uncommitted). The plan cites lines at
4.9.0 (`417c8e58`), and Appendix A gives the shifts at 4.11.0 (`709b3afc`). This audit checks them at `d59843fa` (4.14.6,
worktree `C:/Code/Kronikol-audit135`, branch `audit-135`). Old text was read with `git show <rev>:<path>`.

Labels: **VERIFIED** means read (or run) here at d59843fa. **INFERRED** means reasoned from what was read and not run.
Status: **OK** means the same lines say the same thing. **MOVED** means the same text now sits at new lines.
**CHANGED** means the text changed.

Which cited files changed:
- **417c8e58..709b3afc:** `KafkaTracker.cs`, `TrackingKafkaProducer.cs`, `KafkaTrackingOptions.cs` and
  `KafkaServiceCollectionExtensions.cs`, as §11 says. The cited test file `TrackingKafkaProducerTests.cs` also changed
  (+36 lines, two #136 facts), which §11 and Appendix A do not mention.
- **709b3afc..d59843fa:**
  - `KafkaTrackingOptions.cs` (4.12.1)
  - `Kronikol.csproj` (4.12.1)
  - `GrpcTrackingInterceptor.cs` (4.12.1, 4.13.0, 4.13.2)
  - `RequestResponseLogger.cs` (4.12.2)
  - `TestTrackingMessageHandler.cs` (4.12.1, 4.14.3)
  - `InternalFlowActivityListener.cs` (4.14.3)
  - `ci.yml` (4.11.1, 4.13.3, 4.14.4)

  The rest of the Kafka extension, the build interception, both Kafka test projects, the internal-flow builder and
  collector, `query trace`, `PathEngine`, `CallNesting`, `RequestResponseLog`, the MassTransit, ServiceBus, EventHubs and
  PubSub senders, the ClickHouse fixture and the ClickHouse plan are byte-identical to 4.9.0 or 4.11.0.

## 1. Every in-repo `file:line` citation (sections 0, 1, 3, 4, 5, 6, 8, 11 and Appendix A)

| Plan line | Citation (as written) | Status at 4.14.6 | Current lines | What it says now; does the claim hold? | Label |
|---|---|---|---|---|---|
| 25, 54 | `InternalFlowSegmentBuilder.cs:182-236` | OK | 182-236 | The response-timestamp lookup is at 182-187, start = request time at 206, end = response at 213-214, the -50 ms tolerance at 230, and the span filter at 232-236. Holds | VERIFIED |
| 54 | `InternalFlowSegmentBuilder.cs:67-80` | OK | 67-80 | The whole-test flow takes every span of a trace only this test's calls claim, with no time filter (74-78), plus the kept per-call spans (79-80). Holds, with the nuance that a trace other tests also claim contributes only time-filtered spans | VERIFIED |
| 51 | `KafkaBuildInterceptor.cs:123-143` | OK | 123-143 | `PatchConsumerBuild`/`PatchProducerBuild` with `MakeGenericMethod(TKey,TValue)` and a closed postfix. The file is unchanged since 4.9.0 | VERIFIED |
| 76 | `KafkaBuildInterceptor.cs:123-153` | OK | 123-153 | The patch sites plus the closed postfixes (145-153) | VERIFIED |
| 78 | `KafkaBuildInterceptor.cs:102`, `:108`, `:114` | OK | 102, 108, 114 | The docs "The Harmony patch remains but becomes a no-op" (102, 108) and "removes all Build() patches" (114). `Reset()` still calls `UnpatchAll` (118) | VERIFIED |
| App. A | `KafkaBuildInterceptor.cs` 9-14, 102-153 | OK | same | Class summary 9-14 | VERIFIED |
| App. A | BuildInterception `*.csproj` line 6 | OK | 6 | The `Description` names only ConsumerBuilder/ProducerBuilder. Lib.Harmony floats `2.*` (line 10) | VERIFIED |
| 52 | `KafkaTracker.cs:109-143` | MOVED (+3, as App. A says) | 112-146 | `traceId = Guid.NewGuid()` at 112. Neither `Log` sets `ActivityTraceId`/`ActivitySpanId`. Holds. The consume path is the same at 160-195 | VERIFIED |
| 83 | `KafkaTracker.cs:112-143` | MOVED (+3) | 115-146 | Two `RequestResponseLogger.Log` calls with no `Timestamp` (LogIncoming 164-195 likewise). Holds | VERIFIED |
| 88 | `KafkaTracker.cs:103-107`, `:122-127` | MOVED (+3) | 106-110, 125-130 | Effective verbosity and `body` null on Summarised; `WithVariants` builds variants from the `content` the wrapper passed. Holds | VERIFIED |
| App. A | `KafkaTracker.cs` 28-193; "log methods 31 to 196, identity resolutions `:103` and `:151`" | OK at 4.11.0 numbers | 31-196, 103, 151 | Unchanged since 4.11.0 | VERIFIED |
| 79 | `TrackingKafkaProducer.cs:54-90` | OK | 54-90 | Both sync `Produce` overloads log inside `wrappedHandler` | VERIFIED |
| 80 | `TrackingKafkaProducer.cs:62-68`, `:81-87` | OK | same | The handlers never read `report.Error` | VERIFIED |
| 81 | `TrackingKafkaProducer.cs:24-37`, `:39-52` | OK | same | `await _inner.ProduceAsync` comes before `LogProduce`, so a throw logs nothing | VERIFIED |
| 82 | `TrackingKafkaProducer.cs:62`, `:70` | OK | 62, 70 (also 81, 89) | A non-null wrapped handler is always passed | VERIFIED |
| 84 | `TrackingKafkaProducer.cs:104-114` | MOVED (+1 at end, 4.11.0) | 104-115 | `Headers.Add` at 113-114 still appends. F9 holds | VERIFIED |
| 88 | `TrackingKafkaProducer.cs:94` | OK | 94 | `if (_options.Verbosity == Summarised) return null;` | VERIFIED |
| 160 | `TrackingKafkaProducer.cs:109` ("there", 4.11.0) | OK | 109 | `ResolveWithSource(_tracker.HttpContextAccessor, ...)` | VERIFIED |
| 495, App. A | `TrackingKafkaProducer.cs:108` (417c8e58 coordinates); App. A range 24-114 | MOVED | accessor 109 (comment 108); range 24-115 | App. A still says `:108` while §4.2 says `:109`. Both are right only in their own revision | VERIFIED |
| 85 | `TrackingKafkaConsumer.cs:70-81` (return `:72`, clear `:87`) | OK | 70-81, 72, 87 | `if (result is null \|\| result.IsPartitionEOF) return;` at 72 comes before `EstablishTestIdentityFromHeaders`, whose `ClearMessageIdentity` is at 87 (and only when `PropagateTestIdentity`, 85). F10 holds | VERIFIED |
| 88 | `TrackingKafkaConsumer.cs:122` | OK | 122 | Summarised returns null | VERIFIED |
| App. A | `TrackingKafkaConsumer.cs` 24-43, 70-130 | OK | same | | VERIFIED |
| 86 | `KafkaOperationClassifier.cs:12-15` | OK | 12-15 | Raw `$"{op.Operation} {op.Topic}"`, so `"Commit "` | VERIFIED |
| 87 | `KafkaOperationClassifier.cs:3-5` | OK | 3-5 | "Classifies Apache Kafka HTTP requests ... URL patterns and HTTP methods" | VERIFIED |
| 115 | `KafkaTrackingInterceptor.cs:95-120` | OK | 95-120 | `WrapConsumer`/`WrapProducer` look up the per-pair dictionary | VERIFIED |
| App. A | `KafkaTrackingOptions.cs` "`HttpContextAccessor` (`:67`)" | MOVED (+5, 4.12.1) | 72 | 4.12.1 added a 5-line doc to `CurrentStepTypeFetcher` (20-24: "Has no effect ... removal is an open question for v5"). `HttpContextAccessor` is still the last member | VERIFIED |
| 83 | `RequestResponseLogger.cs:53-54` | MOVED (+6, 4.12.2) | 59-60 | `if (log.Timestamp is null) log = log with { Timestamp = DateTimeOffset.UtcNow };`. Holds | VERIFIED |
| 83 | `RequestResponseLogger.cs:106-110` | MOVED (+22, 4.12.2) | 128-132 | `LogPair`: one `now` for both halves. Holds | VERIFIED |
| 89 | `InternalFlowActivityListener.cs:64` | MOVED + CHANGED (4.14.3) | 66 (body 66-75) | Still `internal static void EnsureStarted`. A caller that loses the start race now waits up to `RegistrationWait` = 1 s (71, 81). New `internal static bool IsStarted` (86). The claim holds | VERIFIED |
| 89, App. A | `Kronikol.csproj:13-23` | CHANGED (4.12.1) | 13-26 | The IVT list now also names `Kronikol.Extensions.ProxyTap` and `Kronikol.Playwright` (21-22, for `TrackingHeaderValue`). Kafka is still absent, so F14's conclusion holds, but "names gRPC and ClickHouse.Driver" is now incomplete | VERIFIED |
| 89 | `GrpcTrackingInterceptor.cs:331` | MOVED (file rewritten in 4.12.1, 4.13.0 and 4.13.2) | 525 (in `EnsureListenerStarted` 522-527) | Still calls `InternalFlowActivityListener.EnsureStarted()` | VERIFIED |
| 180 | `GrpcTrackingInterceptor.cs:335-353` | CHANGED | 529-535, 602-628, 630-647 | `CaptureActivityContext` (529-535) now also returns `Recorded`, with random ids and `false` when no `Activity`. `InjectTraceParent` is gone. `WithCallMetadata` (602-628) copies the caller's metadata into a new `Metadata`, adds the identity headers (`TrackingHeaderValue.Encode`) only when the identity `IsAttributed` (568), and adds `traceparent` only if absent (`AddIfAbsent` 630-634, case-insensitive `HasEntry` 638-647), flags `01` when the span is recorded, else `00` (624). "Random ids with flags `00`" still holds for the no-Activity case. "Flags from the `Activity`, as #134's plan does" is now shipped code | VERIFIED |
| (harness) | `GrpcTrackingInterceptor.cs:18` (research-kronikol.md:258, research-tests-docs.md:219) | MOVED | 20 | `ActivitySource("Kronikol.Grpc")` | VERIFIED |
| 181 | `TestTrackingMessageHandler.cs:156-169` | MOVED + CHANGED (4.14.3) | 164-183 | The no-Activity branch (174-183) is unchanged: random ids, inject `00` when no `traceparent`. **New:** when an `Activity` is current and the transport is not `SocketsHttpHandler`/`HttpClientHandler` (`TransportPropagatesTraceContext` 348-358), the handler injects the current span's `traceparent` with flags from `Recorded` (168-172). "Unlike HTTP, nothing in Confluent injects one when an Activity exists, so Kafka must" still holds, and the HTTP precedent is now closer than the plan says | VERIFIED |
| 53 | `QueryCommand.Trace.cs:97-99`, `:46-50` | OK | same | The chain is the calls whose request `ActivityTraceId` equals the id (OrdinalIgnoreCase). An `sN/iM` address with no id exits 2. C3 holds | VERIFIED |
| 56 | `PathEngine.cs:169` | OK | 169 | A name step matches only when `element.ValueKind == Object` | VERIFIED |
| 223 | `CallNesting.cs:62-76` (`src/Kronikol/Query/`) | OK | 62-76 | `InnermostParent`: the open call whose ServiceName is the request's CallerName, or a different caller sharing the Kronikol TraceId (69-72) | VERIFIED |
| 225-226 | `InternalFlowSpanCollector.cs:11-27`, `:73-90` | OK | same | The well-known list includes `Kronikol.Grpc`, `Grpc.Net.Client`. AutoInstrumentation keeps whole traces that hold a well-known span | VERIFIED |
| 92 | `KafkaBuildInterceptorTests.cs:162-174` | OK | 162-174 | `Different_Type_Pairs_Are_Independent`, `<string,string>` versus `<int,string>`. Across the file only those two pairs appear (21 `<string,string>`, 1 `<int,string>`) | VERIFIED |
| 92 | `TrackingKafkaProducerTests.cs:296-297` | MOVED (+36, at 4.11.0, not in App. A) | 332-333 | The fake's two `Produce` overloads still ignore the handler | VERIFIED |
| 161-162 | Facts `ProduceAsync_injects_the_requests_identity_when_the_options_carry_an_accessor`, `Produce_injects_the_requests_identity_when_the_tracker_was_given_an_accessor` | OK | 274, 292 | Both exist | VERIFIED |
| 327 | `tests/Kronikol.Tests.ClickHouse/ClickHouseServerFixture.cs:12-57` | OK | 12-57 | Env var `KRONIKOL_TEST_CLICKHOUSE`, else Testcontainers; skips only on `DockerUnavailableException` (37-42) via `Assert.SkipWhen` (55). Testcontainers family 4.8.1 (ClickHouse, MongoDb, Redis) | VERIFIED |
| 329 | `CLICKHOUSE_DRIVER_CLIENT_PLAN.md:214-216` | OK | 214-216 | "real-server facts need Docker and skip without it ... falls under the owner's rule, and CI runs them (A2)" | VERIFIED |
| 331 | `ci.yml:277-290` | MOVED (+10) | 287-302 | The remainder step still auto-discovers `tests/*/` minus SKIP. SKIP gained `Kronikol.Tests.xUnit2.Fixtures` (294). The Kafka projects are not skipped. The matrix entry (79-80) has no `os`, so it runs on `ubuntu-latest` (60), which has Docker (INFERRED from the runner image) | VERIFIED |
| 435 | `TrackingConsumeObserver.cs:24-31` | OK | 24-31 | `ClearMessageIdentity()` (27) comes before `LogConsume` (29) | VERIFIED |
| 436 | `MassTransitTracker.cs:105-137` | OK | 105-137 | Two `Log` calls, no `Timestamp` | VERIFIED |
| 439-440 | `TrackingServiceBusSender.cs:44-47`, `TrackingEventHubProducerClient.cs:57-60`, `TrackingPublisherClient.cs:49-52` | OK | same | catch then `LogResponse(..., ex.Message)`. None of the three trackers' `LogResponse` passes a status, and `InteractionStatus.IsError` (`src/Kronikol/Reports/InteractionStatus.cs:101-111`) is false for a null status. Holds | VERIFIED |
| 446 | `KafkaServiceCollectionExtensions.cs` (four registrations prefer the option) | OK | 33, 59, 85, 111 | `options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()` | VERIFIED |
| App. A | `InternalFlowSpanCollector.cs` 11-27 | OK | 11-27 | | VERIFIED |
| §0, §4.1, §11 | "126 facts" / "17 of 17" (4.9.0 baseline) | CHANGED | 139 / 17 | 4.11.0 (#136) added 13 test cases (118 → 125 `[Fact]/[Theory]` attributes); nothing since. The 1.66 to 2.54 s cost measurement was on 126 facts | VERIFIED (run) |
| §11 | "of the files this plan cites only KafkaTracker.cs, TrackingKafkaProducer.cs, KafkaTrackingOptions.cs and KafkaServiceCollectionExtensions.cs changed" (417c8e58..709b3afc) | Incomplete | | `TrackingKafkaProducerTests.cs`, which the plan cites at `:296-297`, also changed | VERIFIED |

Not checked: wiki pages (`Integration-Kafka-Extension.md`, `API-Reference.md`, `Querying-Reports.md`,
`Integration-OpenTelemetry-Extension.md`, `Exporting-to-OpenTelemetry.md`, `Internal-Flow-Tracking.md`,
`Generated-Reports.md`, `Integration-Grpc-Extension.md`, `Event-Driven-Architecture-Testing.md`,
`Parallel-Safe-Background-Correlation.md`, `HTTP-Tracking-Setup.md`, `Diagnostics-and-Debugging.md`), Kronikol4J
(`GoldenHtmlParityTest.java:63`, `ReportDataParityTest.java:36`, `REMAINING_PARITY.md`) and BreakfastProvider
(`KafkaEventPublisher.cs:62`). They are outside this repo and outside the task's scope.

## 2. Claims that are facts about the code, at 4.14.6

| Claim | Holds? | Current evidence | Label |
|---|---|---|---|
| F10: the null/EOF return comes before the clear | Yes | `TrackingKafkaConsumer.cs:72` return, `:87` clear | VERIFIED |
| F8: each half is stamped by `RequestResponseLogger.Log` as it is enqueued | Yes | `KafkaTracker.cs:115-146`, `:164-195` set no `Timestamp`; `RequestResponseLogger.cs:59-60` stamps `UtcNow` per call | VERIFIED |
| 4.7.3 `LogPair`: one instant, no `durationMs` | Yes | `RequestResponseLogger.cs:128-132` (one `now`); `ReportGenerator.ComputeInteractionDurations` (`src/Kronikol/Reports/ReportGenerator.cs:4624-4652`) drops a pair whose elapsed time is not > 0 (4645-4649). A capturer can also set `RequestResponseLog.DurationMs` (`RequestResponseLog.cs:115`), which wins (4634-4637) | VERIFIED |
| F14: `EnsureStarted` internal; IVT names gRPC and ClickHouse.Driver only | Conclusion holds, list stale | `InternalFlowActivityListener.cs:66` internal; `Kronikol.csproj:13-26` adds ProxyTap and Playwright; no Kafka. The only callers are the HTTP handler (`TestTrackingMessageHandler.cs:138`) and gRPC (`:525`) | VERIFIED |
| F16: trackers that record W3C ids are HTTP, gRPC, `MessageTracker`, OTLP, ProxyTap and TcpTap | Yes for 4.14.6, with one omission that also existed at 4.9.0 | Writers: `TestTrackingMessageHandler.cs:261-262, 296-297, 323-324`; `GrpcTrackingInterceptor.cs:484-485, 512-513`; `MessageTracker.cs:144-145, 203-204, 282-283, 308-309`; `Otlp/MappedSpan.cs:89-90, 101-102`; `ProxyTap.cs:391-392, 403-404`; `TcpTap.cs:630-631, 646-647`. **Omitted:** `TrackingProxy` (DispatchProxy/MediatR) in `TrackingLogMode.Deferred` with `ActivitySourceName` set enqueues its activity's ids (`TrackingProxy.cs:101-108`, `:203-210`, `PendingRequestResponseLogs.cs:51-52, 63-64`). §8's "the DispatchProxy ... record no W3C ids" is therefore only true of its immediate mode | VERIFIED |
| F13: `BuildContent` returns null on Summarised before the phase variants | Yes | `TrackingKafkaProducer.cs:94`, `TrackingKafkaConsumer.cs:122`; the variants reuse `content` (`KafkaTracker.cs:125-130`, `:174-179`) | VERIFIED |
| C3: `query trace` keeps the calls whose request carries the id; an address with no id is refused | Yes | `QueryCommand.Trace.cs:97-99`, `:46-50`. The class doc at `:6` still says ids are "exported on every interaction since 3.0.47", the same false claim F16 fixes in the wiki | VERIFIED |
| C2: entries carry no W3C ids; the pair's TraceId is a Kronikol Guid | Yes | `KafkaTracker.cs:112`, `:160` | VERIFIED (read; RUN not repeated) |
| C6: `values` reads responses by default | Yes | `QueryCommand.Values.cs:48-49` | VERIFIED |
| F1, F2, F4, F5, F6, F7 (RUN probes) | Code paths unchanged | Not re-run: read-only audit. The probes ran on Confluent.Kafka 2.15.1 and Lib.Harmony 2.4.2; today's floating restore resolves **2.16.0** and **2.4.3** (section 4) | INFERRED |
| F7 via Confluent | Yes | 2.15.1 `Producer.cs:874-877` throws; the string overload (860-865) delegates to it; in 2.16.0-RC1 the throw is at `:1030` | VERIFIED |
| F9: identity headers appended to the caller's `Message` | Yes | `TrackingKafkaProducer.cs:112-114` | VERIFIED |
| §8 MassTransit, ServiceBus, EventHubs, PubSub | Yes | see section 1 | VERIFIED |
| 4.3: OTLP export uses the captured `ActivityTraceId` when present | Yes | `src/Kronikol.Extensions.Otlp/OtlpSpanMapper.cs:277-282` | VERIFIED |
| §8 "#134 ... planned by other sessions" | Stale | #134 shipped as 4.12.1, 4.13.0, 4.13.2 and 4.14.3 (GRPC_IDENTITY_PROPAGATION_PLAN complete) | VERIFIED (git log) |
| §7 roadmap: D37 sits between D36 and D38 | Yes | `ROADMAP.md:179-181`; row 1.25 (#162) has also been placed since | VERIFIED |
| §0 "the closed postfix is in the package's first commit (`004589fe`)" | Yes | `004589fe` (2026-04-28) has `MakeGenericMethod` and `ProducerBuildPostfix<TKey,TValue>` | VERIFIED |
| F17 coverage gaps | Yes | No fake invokes a delivery handler (four `IProducer` fakes, not five: `KafkaServiceCollectionExtensionsTests.cs:297`, `KafkaTrackingInterceptorTests.cs:241`, `TrackingKafkaProducerFactoryTests.cs:111`, `TrackingKafkaProducerTests.cs:308`); only `Consume(TimeSpan)` is exercised; no null or EOF fact | VERIFIED |

## 3. Confluent citations (source copies in the scratchpad `ck-src/`)

| Citation | Result | Label |
|---|---|---|
| 2.15.1 `Producer.cs:556` / `:570` | `internal Producer(DependentProducerBuilder<TKey,TValue>)` / `internal Producer(ProducerBuilder<TKey,TValue>)`. Exact | VERIFIED |
| 2.16.0-RC1 `Producer.cs:661` / `:692` | Same two constructors. Exact | VERIFIED |
| 2.0.2 `Producer.cs:551` / `:565` | Same two (read from the tarball member `src/Confluent.Kafka/Producer.cs`). Exact | VERIFIED |
| `Consumer(ConsumerBuilder<,>)` (not cited by line) | 2.0.2 `:623`, 2.15.1 `:651`, 2.16.0-RC1 `:669` | VERIFIED |
| `Producer.cs:874-877` (2.15.1) | `if (deliveryHandler != null && !enableDeliveryReports) throw new InvalidOperationException("A delivery handler was specified, but delivery reports are disabled.");` at 874-877 (throw at 876) | VERIFIED |
| Released 2.16.0 (the version CI now resolves) | Reflection on `~/.nuget/packages/confluent.kafka/2.16.0/lib/net8.0/Confluent.Kafka.dll` (FileVersion 2.16.0.0): `Producer\`2` has internal ctors `(DependentProducerBuilder\`2)` and `(ProducerBuilder\`2)`, and `Consumer\`2` has internal `(ConsumerBuilder\`2)` | VERIFIED |

## 4. Test run at d59843fa

Both projects target net10.0 only (no TargetFramework of their own; `Directory.Build.props:5` gives `net10.0`). The
restore resolved Confluent.Kafka **2.16.0** and librdkafka.redist 2.16.0 for both projects, and Lib.Harmony **2.4.3** for
BuildInterception (its nupkg entered the NuGet cache on 2026-10-12). Neither test csproj sets `TieredCompilation*`.

| Project | Result | Wall time (clean build + test) | Wall time (`--no-build`, two runs) | In-test duration |
|---|---|---|---|---|
| `tests/Kronikol.Tests.Kafka` | **139 passed / 0 failed / 0 skipped** (plan: 126 at 4.9.0; +13 from #136 in 4.11.0) | ~12 s | 2.88 s, 2.76 s | 461 ms first run; 150 ms and 165 ms after |
| `tests/Kronikol.Tests.Kafka.BuildInterception` | **17 passed / 0 failed / 0 skipped** (plan: 17) | ~19 s (includes restoring Harmony 2.4.3) | 3.89 s, 5.28 s | 2 s first run; 1 s after |

Warnings only (CS8604, CS1573 in core; xUnit1051 at `TrackingKafkaProducerTests.cs:230`, `:249`). No failures.

Cleanup: no `bin/` or `obj/` existed anywhere in the worktree before the run. Deleted `bin/` and `obj/` of the two test
projects (about 270 MB). Left in place, created by the same build: `src/Kronikol/{bin,obj}` (26 MB),
`src/Kronikol.Extensions.Kafka/{bin,obj}` (20 MB) and `src/Kronikol.Extensions.Kafka.BuildInterception/{bin,obj}` (20 MB).
All are gitignored; `git status` shows only the two untracked plan paths.

## 5. Code at 4.14.6 the plan should cite (new since 4.9.0/4.11.0 unless marked)

1. **gRPC call metadata (4.12.1):** `GrpcTrackingInterceptor.WithCallMetadata` (`:590-628`), `AddIfAbsent` (`:630-634`)
   and the `IsAttributed` gate (`:568`).
   - The caller's metadata is never written to; it is copied. The doc at `:591-593` gives the reason: reuse would stack
     `traceparent`.
   - Headers and `traceparent` are added only when absent.
   - It is the precedent for 4.3's "a message that already has one is not touched". F9's fix (`Headers.Remove` then
     add, latest test wins, on the caller's `Message`) takes the opposite policy from this and from the HTTP handler.
     The plan should cite both precedents and say why. VERIFIED read; the policy point is INFERRED.
2. **HTTP handler (4.14.3):**
   - Identity headers via `AddIfAbsent` (`TestTrackingMessageHandler.cs:223-230`, `:360-364`): "a request sent again
     (a retry) is not stamped twice".
   - A `traceparent` from `Activity.Current` with flags from `Recorded` when the transport does not propagate
     (`:164-172`, `:348-358`). That is the closer analogue to Kafka's injection. VERIFIED.
3. **`TrackingHeaderValue`** (4.12.1, `src/Kronikol/Tracking/TrackingHeaderValue.cs:17-64`, internal; IVT for ProxyTap
   and Playwright).
   - Kafka writes raw UTF-8 bytes (`TrackingKafkaProducer.cs:113-114`) and reads them back, so it needs no change.
   - The resolver decodes HTTP header identities before Kafka writes them (`TestInfoResolver.cs:175`).
   - What matters to the plan: it is why F14's IVT list changed. INFERRED: no Kafka defect.
4. **`RequestResponseLogger.IsolateForTests`** (4.12.2, `RequestResponseLogger.cs:93-107`, internal).
   - Report-level facts (T15 reads `TestRunReport.json`; T35 needs byte-identical reports) are exposed to other tests'
     calls through the run's UnattributedInteractions diagnostic.
   - `Kronikol.Tests.Kafka` has no IVT, so such facts need an IVT entry or must live in `Kronikol.Tests`.
   - The Kafka tests touch the process-wide log on 13 lines (`RequestAndResponseLogs` reads and `Clear()`). INFERRED.
5. **`TestIdentity.IsAttributed`** (`TestInfoResolver.cs:16`; it existed at 4.9.0, and gRPC has used it since 4.12.1).
   - With `RequestResponseLogger.CaptureBackground` on, `InjectTestIdentityHeaders` (`TrackingKafkaProducer.cs:109-114`)
     stamps the background identity `Unknown`/`unknown` on messages.
   - The consumer then calls `SetFromMessage` with it (`TestIdentityScope.cs:172-176`). Its downstream calls resolve with
     `Source = Scope` and read as attributed, and `AutoCorrelateOnConsume` correlates keys to `unknown`.
   - gRPC withholds that identity; HTTP (`:214-230`) does not. A candidate R0 item, on the lines S2 already edits.
     INFERRED, not run.
6. **`FailedSend`** (public since 3.18.0, `src/Kronikol/Tracking/FailedSend.cs:13-30`). This is the "HTTP handler's failed
   send" that F6 and 4.2 invoke (`TestTrackingMessageHandler.cs:274-301`).
   - Its status is `!{ExceptionTypeName}`; the plan's is `!{ErrorCode}` (`!Local_MsgTimedOut`).
     `FailedSend.Status(ProduceException<,>)` would read ``!ProduceException`2``.
   - The plan should cite it and say whether R0 reuses `Describe` for `Error` and why the status departs from it.
   - `InteractionStatus.IsError` (`:101-111`) treats either status as an error. VERIFIED read.
7. **gRPC `StreamOutcome` (4.13.2, `GrpcTrackingInterceptor.cs:383-441`):** it captures the identity, ids and phase at
   the call, logs the response once (Interlocked) when the stream ends or is disposed, and completes the activity. It is a
   shipped template for R0's call-time capture with deferred completion (F4) and for R2's consume response logged
   "when the message is done". VERIFIED.
8. **Kronikol trace id on every hop (4.13.0 gRPC `InboundTraceId` `:571-588`; 4.14.3 HTTP `:220-227`).**
   - Kafka messages carry only `kronikol-test-name`/`-id` (`src/Kronikol/Constants/HttpHeaders.cs:19-23`). A consumer's
     calls therefore never share the producer's Kronikol `TraceId`.
   - `CallNesting` nests on it (`:69-72`).
   - The plan does not discuss this. It may matter for R2's `flow` nesting claim. INFERRED.
9. **Docs added in 4.11.0 that F4 falsifies:** `KafkaTrackingOptions.cs:70` ("a produced message carries the scenario the
   produce is recorded under") and the comment at `TrackingKafkaProducer.cs:108`. For a sync `Produce`, the headers are
   the caller's (B) and the entry is the builder's (A). R0's F4 fix and its doc pass should cite them. INFERRED from F4
   RUN plus reading.
10. **`InternalFlowActivityListener.IsStarted`** (`:86`) and the 1 s registration wait (`:66-75`, 4.14.3): T31 can
    assert the listener started, given the IVT. VERIFIED.
11. **Dependency drift:** CI's floating references now give Confluent.Kafka 2.16.0 (ctors intact, section 3) and
    Lib.Harmony **2.4.3**. F1, F2 and A2 were measured on 2.4.2. Re-run `s0/kronikol` (`pairs`, `tierup`) and `tiermit/`
    on 2.4.3 before R0. VERIFIED versions; behaviour INFERRED unknown.
12. **Pre-existing, omitted:** `TrackingProxy` deferred-mode ids (section 2, F16 row). `QueryCommand.Trace.cs:6` carries
    the "every interaction since 3.0.47" claim in code, so R0's F16 doc fix should cover it too. VERIFIED.

## 6. Harness research-note citations that moved (not plan text, for whoever refreshes the harness)

- `TestTrackingMessageHandler.cs`:
  - `:130-134` → `:136-140`
  - `:154-170` → `:162-183` (changed)
  - `:165-168` → `:178-182`
  - `:254-255` → `:261-262`
  - `:289-290` → `:296-297`
  - `:316-317` → `:323-324`
- `GrpcTrackingInterceptor.cs`:
  - `:18` → `:20`
  - `:328-333` → `:522-527`
  - `:335-357` → `:529-535` plus `:590-647` (rewritten)
  - `:343-357` "always inject" → now inject only if absent (`:623-624`)
  - `:391-399` → `:681-689`
- `TrackingKafkaProducer.cs:118` (`Handle`) → `:119`.
- `ProxyTap.cs:403-404`, `:415-416` → `:391-392`, `:403-404`.
