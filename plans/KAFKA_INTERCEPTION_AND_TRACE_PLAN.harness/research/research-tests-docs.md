# #135 research: Kafka tests, docs, Kronikol4J, plan conventions

Researched 2026-10-07 on the worktree `C:/Code/Kronikol-kafka135` at `417c8e58` (v4.9.0, = origin/main), the wiki at
`origin/master` `db4e473` (4.9.0; the local `C:/Code/Kronikol.wiki` working tree is at `e164dbc`, 4.5.0, 10 commits
behind, clean), Kronikol4J at `4b1332d`. Nothing in any repository was edited. Line numbers are at those commits.
Evidence labels as the plans use them: RUN (measured here), READ (`file:line`), INFERRED, DOC, ISSUE.

## 0. Headlines

1. **New defect, RUN, more serious than the issue's ask 1.** `KafkaBuildInterceptor`'s Harmony postfix on a closed
   reference-type instantiation (e.g. `ProducerBuilder<string,string>.Build()`) replaces the *shared generic code* of
   every reference-type instantiation. With `<string,string>` enabled, `new ProducerBuilder<Null,string>(cfg).Build()`
   returns a `TrackingKafkaProducer<String,String>` that is not an `IProducer<Null,string>`; its first `Produce` throws
   `EntryPointNotFoundException`. Same for `ConsumerBuilder` (`Subscribe`/`Consume` throw). Enabling two reference
   pairs makes the last one win for all (`ProducerBuilder<string,string>` returns `TrackingKafkaProducer<String,Byte[]>`).
   **`KafkaBuildInterceptor.Reset()` does not repair it**: after `UnpatchAll` the other instantiations keep building
   `Producer<String,String>` for the life of the process, even with a no-op postfix and even if nothing was built while
   patched. Producer side and `Reset()` measured with the **shipped** `Kronikol.Extensions.Kafka.BuildInterception`
   4.9.0 from nuget.org on .NET 8.0.27 and 10.0.11 (Lib.Harmony 2.4.2, Confluent.Kafka 2.15.1); consumer side, two
   pairs and the no-op postfix with the same mechanism replicated around the published 4.9.0 wrapper (probes 2, 3, 6).
   Patching `DependentProducerBuilder<string,string>` the same way (the issue's ask 1, probe 4) breaks
   `DependentProducerBuilder<Null,string>` identically. A **prefix that skips
   the shared body and builds `Confluent.Kafka.Producer\`2` for `__instance.GetType()`'s type arguments** (internal type,
   reflection) works for both builders and all pairs (RUN, probe 5). Section 2.
2. Ask 1 reproduced (RUN): `DependentProducerBuilder<string,string>(handle).Build()` returns a raw `Producer` while
   interception is on. Ask 2 reproduced (RUN): under an ambient `Activity`, both entries of a `KafkaTracker.LogProduce`
   have `ActivityTraceId == null`; content is `Key: k, Value: {"a":1}` (ask 3's format).
3. The existing tests cannot see (1): the BuildInterception facts use only `<string,string>` and `<int,string>` (a value
   type key has its own code), so `Different_Type_Pairs_Are_Independent` passes for a reason that does not cover
   reference pairs; the wiki's "patching `<string,string>` does not affect `<Guid,string>`" is true only because `Guid`
   is a value type.
4. **No test anywhere in the repo touches a real Kafka broker.** Every producer/consumer is a hand-written fake
   (duplicated in five files); BuildInterception builds real librdkafka clients against `localhost:9092` with no broker.
   The Testcontainers precedent is `Kronikol.Tests.ClickHouse` (`ClickHouseServerFixture`, Testcontainers 4.8.1), run by
   CI's auto-discovered remainder lane on ubuntu-latest, which has Docker.
5. Docs claim W3C ids are on "every interaction" (`Querying-Reports.md:839`, `Integration-OpenTelemetry-Extension.md:7`);
   false for Kafka. Two wiki pages call a Kafka DI method that does not exist (`services.AddKafkaTracking`).
6. Numbering on origin/main: highest stage-1 row **1.20**, highest decision **D36**. A sibling #134 (gRPC identity
   propagation) is being researched in `C:/Code/Kronikol-grpc134` (untracked `plans/GRPC_IDENTITY_PROPAGATION_PLAN.harness/`,
   no ROADMAP edit yet): agree 1.21/1.22 and D37/D38 before writing. `C:/Code/Kronikol`'s uncommitted
   `ROADMAP.md`/`PLANS_STATUS.md` edits are a stale warm-up session's (at `37e93813`), superseded by origin/main.

## 1. The Kafka tests

### 1.1 Projects

| Project | csproj | Target | References |
|---|---|---|---|
| `tests/Kronikol.Tests.Kafka` | `Kronikol.Tests.Kafka.csproj:1-20` | net10.0 only (inherits `<TargetFramework>net10.0` from `Directory.Build.props:5`) | xunit.v3 3.2.2, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.5.1, coverlet 10.0.0, Microsoft.Extensions.DependencyInjection `10.*`, Microsoft.AspNetCore.Http 2.3.9; project ref `src/Kronikol.Extensions.Kafka` |
| `tests/Kronikol.Tests.Kafka.BuildInterception` | `...BuildInterception.csproj:1-19` | net10.0 only | same xUnit set; project refs to both Kafka packages |

`GlobalUsings.cs` = `global using Xunit;` in both. No mocking library (no NSubstitute/Moq). No `xunit.runner.json`, no
assembly `CollectionBehavior`: xUnit v3 defaults, so test classes run in parallel, tests within a class in sequence.
The packages multi-target `net8.0;net9.0;net10.0` (`Kronikol.Extensions.Kafka.csproj:3-4`,
`...BuildInterception.csproj:3-4`); the reporter runs net8.0, the tests only net10.0. Precedent for multi-targeted
tests: `tests/Kronikol.Tests.AssertionTracking/Kronikol.Tests.AssertionTracking.csproj:4-5` (CI installs 8/9/10 SDKs).
Restored versions (NuGet cache, `project.assets.json`): Confluent.Kafka 2.15.1 (floats `2.*`), Lib.Harmony 2.4.2 (`2.*`).

Counts (static): Kronikol.Tests.Kafka 116 facts + 2 theories (10 rows); BuildInterception 17 facts.

### 1.2 Every test class

| Class (lines) | Covers | Isolation |
|---|---|---|
| `KafkaOperationClassifierTests.cs` (231): 17 facts, 2 theories | `GetDiagramLabel` across Raw/Detailed/Summarised (`:11-107`, `:158-230`, transaction labels as theories `:202-230`) and `BuildUri` (`:113-152`). Raw is tested only with a topic | pure static |
| `KafkaServiceCollectionExtensionsTests.cs` (309): 14 facts | `AddKafkaProducerTestTracking`/`AddKafkaConsumerTestTracking` decorate, keep lifetime, no duplicates, no-op without registration (`:10-117`); the factory DI methods (`:121-217`). Asserts types only; `:76` "Just verify it resolves without error — options are applied internally". No `CurrentTestInfoFetcher`, so nothing is logged. **Namespace is `Kronikol.Extensions.Kafka.Tests` (`:6`)**, every other class `Kronikol.Tests.Kafka` | none needed |
| `KafkaTrackerTests.cs` (431): 32 facts | `KafkaTracker` called directly with a hand-built `KafkaOperationInfo` and a content string: produce pair, `MetaType.Event`, content, Summarised drops content, `TrackProduce=false`, no test info, service/caller, label `Produce → orders-topic`, URI `kafka:///orders-topic` (`:30-141`); consume pair and the caller/service swap (`:145-181`, `:408-430`); Subscribe/Commit/Unsubscribe/Flush/Transaction gating (`:185-210`, `:278-404`); `ITrackingComponent` (`:214-274`) | `TrackingComponentRegistry.Clear()` at `:269` (process-wide; not covered by the guard below) |
| `KafkaTrackingInterceptorTests.cs` (267): 13 facts | `.Tracked()` wraps when enabled, returns the original otherwise, never double-wraps (`:29-114`); Disable/Reset (`:118-154`); `<string,string>` vs `<Guid,string>` (`:158-169`); `WrapConsumer`/`WrapProducer` (`:173-193`) | `IDisposable` → `KafkaTrackingInterceptor.Reset()` (`:11`); the only class in the assembly that touches the interceptor |
| `TrackingKafkaConsumerFactoryTests.cs` (165): 5 facts | factory wraps, delegates, passes `configure`, created consumer logs a consume pair, default factory type | per-test id |
| `TrackingKafkaConsumerTests.cs` (398): 15 facts | Commit (3 overloads) + gating + delegation (`:31-110`); Unsubscribe (`:114-155`); content key/value/Summarised (`:159-228`); propagation: headers establish `TestIdentityScope` (`:232-261`), a headerless message clears the previous identity (`:263-293`), `PropagateTestIdentity=false` (`:295-326`), null headers (`:328-347`) | `[Collection("TestCorrelationStore")]` (`:7-8`) because it calls `TestIdentityScope.Reset()` (`:253`, `:260`, `:274`, `:292`, `:316`, `:325`) |
| `TrackingKafkaProducerFactoryTests.cs` (137): 5 facts | as the consumer factory; created producer logs a `ProduceAsync` pair (`:73-85`) | per-test id |
| `TrackingKafkaProducerTests.cs` (305): 15 facts | Flush (2 overloads) + gating + delegation (`:28-84`); the five transaction methods (`:88-215`); `ProduceAsync` injects `kronikol-test-name`/`kronikol-test-id` (`:219-236`), not when off (`:238-251`); sync `Produce` injects headers (`:253-268`) | per-test id |
| `BuildInterception/KafkaBuildInterceptorTests.cs` (259): 17 facts | Real librdkafka clients, no broker: `new ConsumerBuilder<string,string>(new ConsumerConfig { GroupId = "test-group", BootstrapServers = "localhost:9092" }).Build()` and `new ProducerBuilder<string,string>(new ProducerConfig { BootstrapServers = "localhost:9092" }).Build()` (`:21-30`). Asserts `Assert.IsType<TrackingKafkaConsumer<string,string>>` / `IsNotType` (`:41-77`); `EnableTracking` both (`:81-91`); `Action<>` overload (`:95-108`); Disable/Reset (`:112-145`); idempotent enable (`:149-158`); `<string,string>` vs `<int,string>` with `SetKeyDeserializer(Deserializers.Int32)` (`:162-174`); `BuildTracked()` under the patch does not double wrap (`:178-186`); `Subscribe` through a patched `Build()` logs 2 entries (`:190-202`); re-enable after Disable/Reset (`:206-230`); consumer/producer independence (`:234-258`) | `IDisposable` → `KafkaBuildInterceptor.Reset()` (`:12`); only class in its assembly |

**How assertions read the logger.** Every class: `private readonly string _testId = Guid.NewGuid().ToString();`,
options with `CurrentTestInfoFetcher = () => ("<name>", _testId)`, then
`RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray()` (e.g.
`KafkaTrackerTests.cs:10-15`). `RequestAndResponseLogs` is a snapshot of a static `ConcurrentQueue`
(`src/Kronikol/Tracking/RequestResponseLogger.cs:13`, `:79`); xUnit builds a class instance per test, so the GUID
isolates. Asserted members: count (2 = request + response), `Type`, `MetaType`, `Content` (`Contains`/`Null`),
`ServiceName`, `CallerName`, `Method.Value?.ToString()`, `Uri.ToString()`. Nothing reads `ActivityTraceId`,
`ActivitySpanId`, `Timestamp`, `Phase`, `AttributionSource` or the phase variants.

**Fakes.** Hand-written `IProducer<TKey,TValue>`/`IConsumer<TKey,TValue>` in five files:
`KafkaTrackingInterceptorTests.cs:197-264` (internal), `KafkaServiceCollectionExtensionsTests.cs:221-306`,
`TrackingKafkaConsumerTests.cs:351-395` (flags `CommitCalled`, `UnsubscribeCalled`, `NextConsumeResult`),
`TrackingKafkaConsumerFactoryTests.cs:106-162`, `TrackingKafkaProducerTests.cs:272-302` (flags per method),
`TrackingKafkaProducerFactoryTests.cs:98-134`. Every fake's `Handle` throws `NotImplementedException`, so a
`DependentProducerBuilder` fact needs a real client's handle (a real `ProducerBuilder<,>` with no broker is enough;
`TrackingKafkaProducer.Handle` delegates to the inner, `TrackingKafkaProducer.cs:118`, RUN probe 4). Every fake's
`Produce` ignores its delivery handler (e.g. `TrackingKafkaProducerTests.cs:296-297`), so **the sync `Produce` path's
log (`TrackingKafkaProducer.cs:62-68`, `:81-87`) is never exercised**. `ProduceAsync` returns `new DeliveryResult<,>()`.

### 1.3 Shared static state, and how it is isolated

- `KafkaTrackingInterceptor`: two static `ConcurrentDictionary<Type, TrackingState>` (`KafkaTrackingInterceptor.cs:22-23`),
  `Reset()` clears both (`:84-88`). Only `KafkaTrackingInterceptorTests` touches it in its assembly; no collection.
- `KafkaBuildInterceptor`: static `Harmony` + patched-type dictionaries (`KafkaBuildInterceptor.cs:17-21`); `Reset()` =
  interceptor reset + `HarmonyInstance.UnpatchAll(HarmonyId)` + clear (`:115-121`). Harmony patches are process-wide,
  so any new class in that assembly must be the same class or share one `[Collection]` with it; and (section 2) a
  patch's effect on other reference instantiations survives `Reset()`, so test order matters for any reference pair
  other than the one enabled.
- `TestIdentityScope.Reset()` / `TestCorrelationStore.Clear()`: the guard
  `tests/Kronikol.Tests/Tracking/ProcessWideTrackingStateTests.cs:15-44` scans every `tests/*` project's sources;
  each class with a `[Fact]`/`[Theory]` that calls either must be in a `[Collection("...")]`, one name per assembly
  (here `"TestCorrelationStore"`). A new Kafka class that resets the scope joins that collection.
- `Activity.Current` is AsyncLocal (per test); an `ActivityListener`/`InternalFlowSpanStore` is process-wide. The gRPC
  trace-id facts use the span store without a collection (`tests/Kronikol.Tests.Grpc/GrpcTrackingInterceptorTests.cs:582-675`).
  Patterns to copy: `Assert.Matches(@"^[0-9a-f]{32}$", requestLog.ActivityTraceId)` (`:494-513`, `:565-577`), span id
  16 hex (`:516-535`), `traceparent` injected and equal to the log's ids (`:677-757`); HTTP handler with an ambient
  activity: `new Activity("ExistingTest"); SetIdFormat(ActivityIdFormat.W3C); Start()`
  (`tests/Kronikol.Tests/Tracking/TestTrackingMessageHandlerTests.cs:1705-1720`, `:1735-1763`).
  `MessageTracker` records `Activity.Current` ids (`src/Kronikol/Tracking/MessageTracker.cs:145`, `:204`, `:283`,
  `:309`) but `MessageTrackerTests.cs` never asserts them (0 occurrences).

### 1.4 Coverage gaps (READ), each a candidate red-first fact

| # | Gap | Where |
|---|---|---|
| G1 | Producer content (Key/Value, `LogMessageKey`/`LogMessageValue`) never asserted through `TrackingKafkaProducer` | `TrackingKafkaProducer.cs:92-102` |
| G2 | Sync `Produce` logs only in the delivery handler; no fake calls it | `TrackingKafkaProducer.cs:54-90` |
| G3 | `ProduceAsync(TopicPartition, ...)` untested | `:39-52` |
| G4 | `Consume(int)`, `Consume(CancellationToken)`, `IsPartitionEOF` and null-result skips untested | `TrackingKafkaConsumer.cs:24-43`, `:72` |
| G5 | `Subscribe(IEnumerable<string>)` (one log per topic) untested | `:56-68` |
| G6 | `AutoCorrelateOnConsume`/`ConsumeKeyExtractor` (`TestCorrelationStore.Correlate`) untested | `:102-117` |
| G7 | Phase verbosity (`SetupVerbosity`, `ActionVerbosity`, `TrackDuringSetup/Action`, `WithVariants`) untested for Kafka | `KafkaTracker.cs:33`, `:103`, `:122-127` |
| G8 | Raw label for an op without a topic is `"Commit "` (trailing space: `$"{op.Operation} {op.Topic}"`); the wiki shows `Commit` | `KafkaOperationClassifier.cs:12-15`; wiki `Integration-Kafka-Extension.md:41-47` |
| G9 | No fact that every `IProducer`/`IConsumer` member delegates (noted already by `CLICKHOUSE_DRIVER_CLIENT_PLAN.md:77-78`) | `TrackingKafkaProducer.cs:11`, `TrackingKafkaConsumer.cs:11` |
| G10 | Build interception: no reference-type second pair, no `Null` key, no `DependentProducerBuilder`, net10.0 only | `KafkaBuildInterceptorTests.cs:162-174` |
| G11 | No fact reads `ActivityTraceId`/`ActivitySpanId` (they are never set: `KafkaTracker.cs:109` makes a fresh `Guid` per pair and sets neither) | `RequestResponseLog.cs:69-70` |
| G12 | Doc comment of `KafkaOperationClassifier` says "Classifies Apache Kafka HTTP requests ... URL patterns and HTTP methods" | `KafkaOperationClassifier.cs:3-5` |

### 1.5 A real broker?

- **Kronikol (tests/, examples/, templates/): none.** `grep -ri kafka examples templates` is empty; Testcontainers is
  used only by `Kronikol.Tests.ClickHouse` (`Testcontainers.ClickHouse` 4.8.1, `.csproj:20`) and `Kronikol.Tests.TcpTap`
  (`Testcontainers.Redis`/`MongoDb` 4.8.1, `.csproj:28-29`).
- The precedent to copy: `tests/Kronikol.Tests.ClickHouse/ClickHouseServerFixture.cs:12-57` (an `IAsyncLifetime`: the
  server an env var names, `KRONIKOL_TEST_CLICKHOUSE`, else Testcontainers; skip with the reason only on
  `DockerUnavailableException`, any other start failure fails; `Require()` = `Assert.SkipWhen(...)`), used as an
  `IClassFixture` by `ClickHouseDriverClientRealServerTests.cs:17`. TcpTap's style: `Assert.SkipWhen(!DockerAvailable(),
  "Docker is not available.")` (`ContainerEndToEndTests.cs:53`). The ClickHouse plan's justification for a Docker skip
  under the owner's skip rule: `CLICKHOUSE_DRIVER_CLIENT_PLAN.md:214-216`; its log shows CI ran the 9 real-server facts
  in the remainder lane, none skipped (`:400-402`). A `Testcontainers.Kafka` 4.8.1 would match the pinned family
  (its default image and KRaft support not checked here).
- **BreakfastProvider** (`C:/Code/BreakfastProvider`, local checkout stale: `0d29537`, Kronikol 3.29.0; `106702b`, the
  commit the warm-up plan used, is not present, so use a scratch clone): a docker lane runs a real Kafka
  (`docker/docker-compose-messaging.yml`, SASL_SSL; `.github/workflows/_tests.yml:58-80` certificates, `:97-101` compose
  up). `UseTrackedKafkaProducer` wraps its `IProducerFactory` with `TrackingKafkaProducer` in every lane at
  **Summarised** verbosity (`tests/BreakfastProvider.Tests.Component.Shared/Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs:373-390`,
  `Fakes/Kafka/TrackingKafkaProducerFactory.cs`; called at `...BDDfy/Infrastructure/BaseFixture.cs:461`,
  `...LightBDD/...:518`, `...NUnit/...:462`, `...ReqNRoll/Support/AppManager.cs:393`). Its consumes are drawn by
  `MessageTracker`, not `TrackingKafkaConsumer`; no build interception. Its `KafkaEventPublisher` starts
  `ActivitySource.StartActivity("KafkaPublish", ActivityKind.Producer)` around the produce
  (`src/BreakfastProvider.Api/Events/KafkaEventPublisher.cs:62`): a ready consumer check for ask 2 (produce entries
  should then carry that span's ids), but not for ask 3 (Summarised logs no content).
- Kronikol4J: `docs/REMAINING_PARITY.md:58-60` says broker setup "is solved: single-node KRaft
  `confluentinc/cp-kafka:7.6.1`, host `localhost:19092`"; nothing committed uses it.
- This machine: podman in WSL (`wsl -d podman-machine-default -u root -- ...`), as the ClickHouse harness started its
  server (`plans/CLICKHOUSE_DRIVER_CLIENT_PLAN.harness/start-ch.sh`, README).

### 1.6 CI

Both Kafka test projects run only in **"Remaining Unit Tests (auto-discovered)"** (`.github/workflows/ci.yml:79-80`,
ubuntu-latest `:60`, SDKs 8/9/10 `:210-216`): the step loops `tests/*/` with a csproj, skipping the `SKIP` list
(`:283`, Kafka not in it), and runs `dotnet test "$dir" --configuration Release --verbosity normal --
RunConfiguration.TreatNoTestsAsError=true` (`:277-290`). A new project (e.g. a broker test project) is covered
automatically. `release.yml:37` runs only `tests/Kronikol.Tests`. `release.slnf:26-27` packs both Kafka packages.
No workflow mentions Kafka.

## 2. Probes run for this research (RUN)

All in the scratchpad (`.../scratchpad/probe-*`, each with `Program.cs` and `results.txt`), console apps targeting
net8.0 and net10.0, run on .NET 8.0.27 and 10.0.11, Confluent.Kafka 2.15.1, Lib.Harmony 2.4.2, no broker
(`BootstrapServers = localhost:9092`). Probes 1 to 6 replicate `KafkaBuildInterceptor`'s mechanism verbatim (a closed
`<TKey,TValue>` postfix per enabled pair, `KafkaBuildInterceptor.cs:123-153`; `Reset()` = `UnpatchAll(id)`, `:115-121`)
around the published `Kronikol.Extensions.Kafka` 4.9.0 wrapper; `probe-shipped` uses the published
`Kronikol.Extensions.Kafka.BuildInterception` 4.9.0 itself. Identical output on both runtimes.

| Probe | What it shows |
|---|---|
| `probe-shipped` (the shipped package) | `EnableProducerTracking<string,string>`: `ProducerBuilder<string,string>` → `TrackingKafkaProducer<String,String>`; `DependentProducerBuilder<string,string>(ss.Handle)` → raw `Producer<String,String>` (**ask 1 reproduced**); `ProducerBuilder<Null,string>` → `TrackingKafkaProducer<String,String>`, `is IProducer<Null,string>` False, `Produce` throws `EntryPointNotFoundException: Entry point was not found.`; **after `KafkaBuildInterceptor.Reset()`** `ProducerBuilder<Null,string>` → `Producer<String,String>`, still not an `IProducer<Null,string>`. Under an ambient `new Activity("probe").Start()`, `KafkaTracker.LogProduce` writes 2 entries with `ActivityTraceId` `[null, null]` (**ask 2 reproduced**), content `Key: k, Value: {"a":1}` (ask 3's format) |
| `probe-harmony` | Same with the replicated patch; the postfix closed over `<String,String>` runs for the `<Null,string>` and `<string,byte[]>` builds and is handed a `Producer<String,String>` (Harmony compiles the replacement from the closed `<string,string>` IL, so `newobj Producer<string,string>` is baked into the shared code). `<int,string>` is untouched (value type, own code) |
| `probe-harmony2` | A **no-op** postfix (what "Disable…: the patch remains but becomes a no-op" leaves, `KafkaBuildInterceptor.cs:102`, `:108`) already breaks `<Null,string>`: `Producer<String,String>`, `EntryPointNotFoundException` on `Produce`; consumers too: `ConsumerBuilder<Null,string>` → `Consumer<String,String>`, `Subscribe` and `Consume` throw `EntryPointNotFoundException` |
| `probe-harmony3` | Control (no patch): every builder returns its own type. Two reference pairs enabled (`<string,string>` then `<string,byte[]>`): all three of `<string,string>`, `<string,byte[]>`, `<Null,string>` return `TrackingKafkaProducer<String,Byte[]>` (the last patch wins; `<string,string>` itself broken). `<Null,string>` enabled first: `<string,string>` and `<string,byte[]>` broken |
| `probe-harmony4` | Patching `DependentProducerBuilder<string,string>.Build()` the same way: `<string,string>` tracked; `DependentProducerBuilder<Null,string>` → `TrackingKafkaProducer<String,String>` (broken); `<int,string>` fine. A `DependentProducerBuilder` built from a `TrackingKafkaProducer`'s `Handle` works (the handle delegates) |
| `probe-harmony5` | **A working mechanism:** one non-generic `Prefix(object __instance, ref object __result)` that builds `typeof(IProducer<,>).Assembly.GetType("Confluent.Kafka.Producer\`2").MakeGenericType(__instance.GetType().GetGenericArguments())` through its internal constructor (`Activator.CreateInstance(..., NonPublic, [__instance])`, which picks the `ProducerBuilder<,>` or `DependentProducerBuilder<,>` overload), wraps it with `KafkaTrackingInterceptor.WrapProducer<K,V>` (generic via reflection), and returns `false` so the shared body never runs. Result: `ProducerBuilder<string,string>` and `DependentProducerBuilder<string,string>` tracked; `<Null,string>`, `<string,byte[]>`, `DependentProducerBuilder<Null,string>` return their own `Producer<,>`, and `Produce` on `<Null,string>` works. Cost: depends on Confluent's internal type and constructors (the floating `2.*` test reference would catch a change) |
| `probe-harmony6` | `UnpatchAll` never restores: whether `<Null,string>` was built while patched, only after unpatching, or with no build at all between patch and unpatch, it returns `Producer<String,String>` afterwards (`Harmony.HasAnyPatches` False). So a fix cannot rely on `Reset()` unpatching; with the prefix design the patch stays for the process and "disabled" means `WrapProducer` returns the inner unwrapped |

Consequences for the plan (INFERRED from the above): the defect is a patch-level bug in shipped code (rule 1); the
`DependentProducerBuilder` ask must not be built with the current postfix design; `Reset()`'s doc (`KafkaBuildInterceptor.cs:114`
"removes all Build() patches") and the wiki's "Full cleanup ... removes all Harmony patches" / "Always call Reset() ...
to clean up Harmony patches between tests" (`Integration-Kafka-Extension.md:511-515`) and the independence claim
(`:527`) are wrong today; value-type pairs (`<int,string>`, `<Guid,string>`) have their own code and need their own patch.
Not probed: Harmony's own documentation on generics (DOC, to cite), consumer side with the prefix design (same shape
expected: `Confluent.Kafka.Consumer\`2` internal ctor), `OpenTelemetry.Instrumentation.ConfluentKafka`'s
`InstrumentedProducerBuilder` subclass (an override of `Build()` is a different method and would not be patched).

## 3. Documentation

### 3.1 Wiki pages that mention Kafka (origin/master `db4e473`)

The local checkout is behind origin; of these pages only `API-Reference.md`, `Phase-Aware-Tracking.md`, `FAQ.md`,
`Generated-Reports.md`, `Querying-Reports.md`, `Component-Diagrams.md`, `Diagnostics-and-Debugging.md` differ, so the
plan must "pull first, edit in a worktree of its own" as `WARM_UP_PLAN.md:463` did.

| Page:line | Gist |
|---|---|
| **`Integration-Kafka-Extension.md`** (604 lines) | The extension page. `:7-11` how it works; `:23-48` verbosity and label tables (`:27` Raw URI `kafka:///orders-topic/2@42`; `:41-47` Raw `Commit`, see G8); `:52-67` classified operations; `:71-181` Options A to D (manual wrap, shared tracker, DI `AddKafkaProducerTestTracking`/`AddKafkaConsumerTestTracking`); **`:179` manual DI passes `new KafkaTrackingOptions()` to the producer instead of the tracker's options** (doc bug); `:185-213` options table (`:209` `PropagateTestIdentity`, `:210` `AutoCorrelateOnConsume`, `:211` `ConsumeKeyExtractor`, `:213` dual resolution); `:215-231` selective tracking; `:235-242` direction; **`:246-254` "Consumer Content": `Key: <value>`, `Value: <value>`, example `Key: order-123, Value: {"amount":99}`** (ask 3 edits here); `:258-320` consumer patterns, transactions; `:324-529` internally built clients: Option E `BuildTracked()`/`Tracked()` `:330-382`, F factories `:384-436`, choosing `:438-445`, **G Build interception `:447-529`** (`:451` "any call to `.Build()` — anywhere in the process — automatically returns a Tracking…", false for `DependentProducerBuilder`; `:504-515` disable/Reset; `:517-522` how it works; `:524-528` compatibility, **`:527` independence claim, false for reference pairs**); `:532-538` wrapper transparency; `:542-546` invocation validation; `:550-603` test identity propagation (`:592-601` header format). **No section on trace ids, `traceparent`, Activity diagrams/flame charts or `kronikol query`** (the gRPC page has one: `Integration-Grpc-Extension.md:441-452`, the pattern to copy) |
| `API-Reference.md:245-258` | Kafka section. **`:250` options list lacks** `TrackUnsubscribe`, `TrackFlush`, `TrackTransactions`, `PropagateTestIdentity`, `AutoCorrelateOnConsume`, `ConsumeKeyExtractor`; **`:255` `KafkaOperation` lacks the five transaction members** (`KafkaOperation.cs:15-19`); no rows for `KafkaTrackingInterceptor`, `KafkaBuildInterceptor`, the factories, `Tracked`/`BuildTracked`. `:23` `RequestResponseLog` "Includes ... `ActivitySpanId`, `ActivityTraceId` for OTel correlation" |
| `Event-Driven-Architecture-Testing.md` | `:17-33` identity chain naming `TrackingKafkaProducer`/`TrackingKafkaConsumer`; **`:98-108` "Example: Kafka" calls `services.AddKafkaTracking(new KafkaTrackingOptions{...})`, which does not exist** (the DI methods are `KafkaServiceCollectionExtensions.cs:23`, `:48`, `:72`, `:98`); `:113` `AddServiceBusTracking` does not exist either; `:122`, `:214`, `:353` links |
| `Parallel-Safe-Background-Correlation.md` | `:28` async consumers already work; `:62` messaging extensions populate the store on consume (`AutoCorrelateOnConsume`); `:148-176` decoupled Kafka consumer guide, **`:168` the same non-existent `AddKafkaTracking`**; `:237` `CorrelationKeys.Kafka` format; `:342` link |
| `Background-Thread-Correlation.md` | `:58` `ClearMessageIdentity` (3.17.0); `:193` priority 4 = message headers; `:203-249` "Solution 1b: automatic message header propagation (v2.34.0+)", `:225` Kafka row (`message.Headers` byte[]), `:236` options example |
| `Internal-Flow-Tracking.md` | **`:15` "`TestTrackingMessageHandler` and `MessageTracker` record `Activity.Current?.TraceId` ..."** (Kafka absent; gRPC also records); **`:17` a call that records no trace id takes what its scenario's traced calls could take; where several scenarios' calls record one trace (an ambient `Activity`) a call takes its own span and its descendants** (bears on a long-lived consumer loop's activity); **`:115-131` the `AutoInstrumentation` source list (no Kafka source; code `src/Kronikol/InternalFlow/InternalFlowSpanCollector.cs:11-26`)**; `:279` ingested calls join by `activityTraceId` |
| `Querying-Reports.md` | **`:837-857` `trace`: "Follows a W3C trace id — the `activityTraceId` every interaction carries since 3.0.47"** (false for Kafka) and the cross-scenario warning ("a trace id that leaks across scenarios is the classic flaky-test smell"); `:157-159` trace takes a span id; **`:612-648` `values --path`: default target is the response body, `--request`/`--both` for requests, non-JSON bodies footnoted** (Kafka content sits on the request half, `KafkaTracker.cs:107` vs `:131` where the response carries `null`, so the envelope is reachable only with `--request`); `:573` a `CONSUME (KAFKA)` row (BreakfastProvider's `MessageTracker` consume) |
| `Generated-Reports.md` | **`:274` `traceId` is Kronikol's pair id, not W3C; `:295` `activityTraceId`/`activitySpanId` "the W3C ids — the bridge ... Consumed by `kronikol query trace`"**; `:1087` messaging services are the usual casualties of header propagation breaks |
| `Integration-OpenTelemetry-Extension.md:7-8` | "The W3C trace ids that flow through either path end up on **every interaction** in `TestRunReport.json` ... followable with `kronikol query trace`" (false for Kafka) |
| `Exporting-to-OpenTelemetry.md:64-67`, `:155` | A captured W3C id always wins on export (D4); else `TraceIdStrategy.PerTest`. Code: `src/Kronikol.Extensions.Otlp/OtlpSpanMapper.cs:277-282`. **Ask 2 moves exported Kafka spans from the per-test trace into the app's trace**: a visible change to call out |
| `Integration-Grpc-Extension.md:441-452` | "Activity Diagrams & Flamecharts": own `ActivitySource("Kronikol.Grpc")`, ids on every entry, lazy `InternalFlowActivityListener`, `traceparent` injected, sources in `WellKnownAutoInstrumentationSources`. Code: `src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs:18`, `:335-341` (Activity.Current else random ids), `:353` (`traceparent`) |
| `Diagnostics-and-Debugging.md:412-419`, `HTTP-Tracking-Setup.md:129`, `:189-191`, `:234-241`, `:286-287` | `KafkaTracker` construction examples; dual resolution; Kafka in the DI-extension version table |
| `FAQ.md:9` (`.BuildTracked()` as the one-token change), `:21`, `:65`; `How-To-Guides.md:47` | pointers |
| `Event-Annotations.md:11`, `:17`, `:37`, `:69`, `:186-236`, `:270`, `:326-345`, `:493`, `:507` | `MessageTracker` examples using Kafka as the transport (not the extension) |
| `Component-Diagrams.md:369-381`, `Excluding-Requests.md:39`, `Filtering-and-Redacting-Diagram-Content.md:20` (2,000-char truncation applies to Kafka), `Phase-Aware-Tracking.md:223`, `Tracking-Custom-Dependencies.md:18`, `:374`, `Tracking-Dependencies.md:49`, `:1077`, `:1428`, `:1435-1484` (DecorateAll example with `KafkaTracker`), `_Sidebar.md:66` | passing mentions |

No wiki page mentions `DependentProducerBuilder`, `traceparent` on Kafka, or a JSON envelope.

### 3.2 Repo docs

- `README.md:277` Kafka row ("Wraps `IProducer` and `IConsumer` to track Produce/Consume operations with configurable
  verbosity"), `:278` Build Interception row ("Automatic `ConsumerBuilder`/`ProducerBuilder.Build()` interception via
  Harmony for zero-prod-change Kafka tracking"), `:100` `MessageTracker` for Kafka. `nuget-readme.md`: nothing.
- Package descriptions: `src/Kronikol.Extensions.Kafka/Kronikol.Extensions.Kafka.csproj:6`,
  `src/Kronikol.Extensions.Kafka.BuildInterception/....csproj:6` (names only the two builders).
- Agent text that states what `trace` reads: `src/Kronikol/Reports/agent-instructions.md:128`;
  `templates/skills/kronikol-test-debugging/SKILL.md:118`, `:164-165`, `:259-263` and `references/commands.md:25`,
  `:287`, `:375-377` (each also under `.claude/skills/kronikol-test-debugging/`). Accurate as written; they become true
  for Kafka after ask 2.

### 3.3 CHANGELOG history of the extension

| Version (CHANGELOG line) | Gist |
|---|---|
| 2.17.0, 2026-04-21 (`:8306-8314`) | New `Kronikol.Extensions.Kafka`: `KafkaTracker`, wrapper producer/consumer, classifier, `kafka:///` URIs, consume swaps caller/service |
| 2.22.15 (`:8114`) | Phase-aware trackers, Kafka among 21 |
| 2.23.0 (`:7982`) | Dual resolution (`IHttpContextAccessor`) across extensions incl. Kafka |
| 2.23.12, 2026-04-27 (`:7894-7901`) | `AddKafkaProducerTestTracking` / `AddKafkaConsumerTestTracking` |
| 2.27.7, 2026-04-28 (`:7742-7754`) | Commit (3 overloads), Unsubscribe, Flush tracked; the consumer logs the key; `TrackFlush`, `TrackUnsubscribe` |
| 2.27.8 (`:7734-7740`) | Transactional producer tracking, `TrackTransactions` |
| 2.27.9 (`:7724-7730`) | `KafkaTrackingInterceptor` (`.Tracked()`, `.BuildTracked()`), factory interfaces and their DI |
| **2.27.16, 2026-04-28 (`:7669-7677`)** | **New package `Kronikol.Extensions.Kafka.BuildInterception`** (commit `004589fe`): Harmony postfix on both `Build()`s, Enable/Disable/Reset |
| 2.28.19 (`:7459`) | CS0419 cref fix in `KafkaTrackingInterceptor.cs` |
| **2.34.0, 2026-05-13 (`:6290-6295`)** | **`PropagateTestIdentity`** (default true): `kronikol-test-name`/`-id` headers, `TestIdentityScope.SetFromMessage`, `TestTrackingMessageHeaders` |
| **2.36.0, 2026-05-15 (`:6202-6215`)** | `CorrelationKeys.Kafka`, **`AutoCorrelateOnConsume`** (default true), `ConsumeKeyExtractor` |
| 3.0.0, 2026-05-17 | Rebrand from TestTrackingDiagrams (`4f79ae10`) |
| **3.15.1, 2026-09-14 (`:4250-4264`)** | "Every capture carries a timestamp": Kafka's entries had none; `RequestResponseLogger.Log` stamps them. **Shipped as a patch** with "Report output changes" called out: the closest precedent for adding ids to Kafka entries |
| 3.17.0, 2026-09-15 (`:4164`, `:4200-4210`) | Provenance (`AttributionSource`); a message's identity ends with the message (`ClearMessageIdentity` in the Kafka wrapper); hosted services detached |
| 3.27.3 (tests only, `b5cff009`) | `TrackingKafkaConsumerTests` joins the `TestCorrelationStore` collection, with the guard |
| 4.5.0, 2026-10-01 (`:474`, `:509-512`) | Keyed DI registrations decorated (fixes `AddKafka*TestTracking` too) |

`CHANGELOG.md` opens with `## [4.9.0] - 2026-10-07` (`:7`): no `## [Unreleased]` section to fold in.

## 4. Kronikol4J

- Kafka lives in `kronikol4j-messaging` (`kafka-clients` `compileOnly`):
  `TrackingKafkaProducer.java` (75 lines) wraps a `Producer` in a JDK dynamic proxy: on `send` it stamps the identity
  headers (`KafkaTestHeaders.stamp`) and calls `MessageTracker.trackSendMessage(label, topic, uri, record.value())`
  at send time (offset unknown, `:33-46`); flush/transaction methods → `trackEvent` (`:48-62`).
  `TrackingKafkaConsumer.java` (98) proxies a `Consumer`: on `poll`, **only records carrying the identity headers** are
  tracked, inside a `TestIdentityScope` (`:80-96`; .NET tracks every consume); subscribe/unsubscribe/commit →
  `trackEvent`. `KronikolKafkaFactoryBeanPostProcessor.java` decorates Spring Kafka's factories (the Java analogue of
  build interception; `REMAINING_PARITY.md:1769-1790` records that Harmony's builder interception has no Java form:
  no `ProducerBuilder` in kafka-clients, a JVM constructor cannot return a substitute).
- What it records: **no `activityTraceId`/`activitySpanId`** (`MessageTracker.java:193-203` never sets them; only the HTTP
  modules, the Spring WebClient filter and the opt-in `kronikol4j-opentelemetry` `OtelBridge` do); `traceId` is a fresh
  UUID per record, so a send's request and response get different `traceId`s (`:199`), pairing by
  `requestResponseId`; content = the **value alone** serialised by `TrackingSafeSerializer` (compact JSON,
  `MessageTrackerOptions.java:19-21`), no key; response status `Sent` (send) / `Ack` (consume).
- Standing gap: `docs/REMAINING_PARITY.md:46-61` "**Kafka — DECISION NEEDED**": status (Java `Sent`/`Ack`, .NET null),
  content (Java value only, .NET `Key: <k>, Value: <v>`), participant direction; flagged for the owner before landing.
- **Ledger**: `docs/REMAINING_PARITY.md`, section "## .NET-side features shipped after this audit (divergence ledger)"
  (`:1877`), one bullet per .NET release, appended at the end (latest `:2413`, commit `4b1332d` "Ledger: .NET 4.9.0 ...").
  Format: `- **<What changed> (.NET <x.y.z>, <date>[, #<issue>]).** <Not mirrored | Already the port's behaviour |
  Nothing to mirror>, a ledger entry only[: <why>]. <What .NET now does>. <What the port does>.` Extension example
  (`:2406`): "**ClickHouse.Driver's `IClickHouseClient` is tracked (.NET 4.5.0, 2026-10-01).** Not mirrored, a ledger
  entry only: a .NET-only capture surface. ..." For #135: build interception (incl. the shared-code fix) has nothing
  to mirror; trace ids on Kafka entries are not mirrored (the port records none on any messaging entry); a JSON
  envelope option is .NET-only (the port already logs the value alone as JSON, so its content stays different).
  The ledger line follows publication; the plan's Log records its commit.

## 5. Plan conventions

### 5.1 `plans/PLANS_STATUS.md`

Header `:1-16`; table header `:18-19` `| Plan | Status | Shipped in |`; newest row first (inserted at `:20`). A
not-yet-green-lit row (from the stale warm-up session's diff in `C:/Code/Kronikol`, the form that was committed first):

> | `WARM_UP_PLAN.md` *(new, 2026-10-06)* | ❌ Plan written, nothing implemented, **NOT green-lit**; `ROADMAP.md` row 1.19 and decision D33. Plans #113: ... Proposes R0 (patch: ...), R1 (minor: ...) and R2 (minor: ...). Measured on ...; harness `WARM_UP_PLAN.harness/`; Q5 (the badge's look) is the owner's | — |

Two rows verbatim as they stand on origin/main:

```
| `CLICKHOUSE_DRIVER_CLIENT_PLAN.md` *(new, 2026-10-01)* | ✅ **EXECUTED 2026-10-01** (`ROADMAP.md` D32, row 1.16), with §9's recommendations. **R1 = 4.4.1**: a failed SQL statement is drawn with its error, in every SQL wrapper that shared the gap (F10, F13). **R2 = 4.5.0**: `TrackingClickHouseClient`, `TrackingClickHouseDataSource` and the DI decoration, with a real-server test lane; keyed DI decoration fixed on the way (F14). Plans #126: calls through ClickHouse.Driver's `IClickHouseClient`, `InsertBinaryAsync` among them, are never recorded. The issue's repro reproduced verbatim on 4.4.0 against ClickHouse 25.8 (RUN). The ask is buildable except for a data source's connections (F2). Four limits the issue does not state shape the design. The driver's recommended DI shape is the sealed concrete `ClickHouseClient` (F1). The client's `ExecuteNonQueryAsync` returns 0 for every write (F3, RUN). The driver reads the caller's rows by reference at send time, so the record is formatted after the call (F4, RUN: a reused array was stored as `12,12,12`). A hand-written decorator breaks when the driver adds an interface member (F5). **F10 (RUN):** a statement the server rejects, through the shipped tracked connection, records a request and no response. R1 fixes it as a patch ahead of R2's minor. Nine open questions; the probe is in `CLICKHOUSE_DRIVER_CLIENT_PLAN.harness/` | 4.4.1 (R1), 4.5.0 (R2), both published 2026-10-01 |
| `WARM_UP_PLAN.md` *(new, 2026-10-06)* | ✅ **Executed 2026-10-07 as 4.7.3, 4.8.0 and 4.9.0**, green-lit the same day ("complete the plan in full"; `ROADMAP.md` row 1.19, D33 taken: Q5 = A, the warm-up as muted text in the badge, every other question as recommended). **R0 shipped as 4.7.3** (2026-10-07): durations and dates written invariantly, and section 8's patch rows. **R1 shipped as 4.8.0** (2026-10-07): the warm-up pass, `warmUpSeconds` and `warmUp` in the data files, and the CLI's lines. **R2 shipped as 4.9.0** (2026-10-07): the badge, the timeline and the duration filter; leftovers in `ROADMAP.md` Appendix C. Plans #113: a scenario's duration carries the app's one-time warm-up (the run's first request down a path, 55 to 115 times a warm one), charged to whichever scenario goes first, and nothing says so. R0 (patch: XML/YAML durations, timeline widths and `kronikol query` durations written with a comma on comma-decimal machines, F12, and the defects of its section 8), R1 (minor: a run-wide pass marking first-call warm-ups, `warmUpSeconds` and `warmUp` in the data files and schemas, carried by merge, `summary`'s Slowest ranked without it, `flow` and `interactions` marking the calls) and R2 (minor: badge marker, timeline shading, the P50 to P99 filter by time left). Measured on BreakfastProvider's 18 lanes, local runs and `examples/Example.Api`: the rule explains 25 of 57 in-memory anomalies (the issue's rule, 15) and over-subtracts none; harness `WARM_UP_PLAN.harness/` | — |
```

### 5.2 `plans/WARM_UP_PLAN.md`, the first 60 lines verbatim

````markdown
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
````

### 5.3 Section structure (headings in order)

`WARM_UP_PLAN.md` (697 lines): `# Warm-up plan: #113` → header paragraph (**Written:** date, at version (`commit`),
days after the issue; **Status:**; evidence labels RUN/READ/INFERRED/ISSUE/DOC; harness link) → intro (the ask, what
the plan changes and why, defects found outside the ask) → `## 0. Summary` (two-column table: What ships, The rule,
What it records, What it cannot see, Bumps, Waits for, Open) → `## 1. How far each claim was checked` (C1.. table:
The issue says | Level | Verdict) → `## 2. Where it stands today` → `## 3. Findings the issue does not state` (F1..
table: Finding | Level | Where) → `## 4. The design` (4.1 to 4.8) → `## 5. Tests, red first` (T table + mutations
table) → `## 6. Slices, releases and records` (6.1 Slices, 6.2 Releases, 6.3 Changelog drafts, 6.4 Wiki, 6.5
Kronikol4J, 6.6 Before declaring done) → `## 7. Where it sits in the roadmap` → `## 8. Found on the way, not in this
plan` → `## 9. Questions, with recommendations` (Q table) → `## 10. Assumption ledger` (A table) → `## 11. Log` →
`## Appendix A. Edit sites at <commit>`.

`CLICKHOUSE_DRIVER_CLIENT_PLAN.md` (407 lines, the extension-package plan) has the same skeleton: header (`:1-9`),
intro with the four limits (`:11-26`), `## 0. Summary` (`:28-37`: What ships, What it records, What it cannot record,
What it needs first = "A real-server test lane (S1)", Bumps, Open), `## 1` claims C1-C10, `## 2` (with a "The code
holds | Today | After R2" table, `:63-74`), `## 3` F1-F14 (F13/F14 added during execution), `## 4. The design`
(4.1 The surface: a member table; 4.2 What each member records; 4.3 rows; 4.4 Failures; 4.5 DI; 4.6 What stays
untracked and what the wiki says about each; 4.7 When the driver adds an interface member), `## 5. Tests, red first`
(`:212-245`), `## 6` (6.1-6.6), `## 7` to `## 11`. Variants: `PHASE_FROM_STEPS_PLAN.md` opens with `## TL;DR`, then
`## 0. Findings`, and puts tests in `## 4. Tests, in the order they are written` (4.1 new facts, 4.2 existing facts,
4.3 mutations, 4.4 proofs before the tag), `## 9. What is not known`, `## 10. Risks and traps`, `## Log` last.

### 5.4 Naming

- **Releases R0/R1/R2**: R0 = a patch for live defects found on the way, shipped first and alone (rule 1, rule 5 when
  it edits the same lines); R1, R2 = the ask, each with its bump. A version number is taken only when cut ("until then
  this plan says R0, R1, R2", `WARM_UP_PLAN.md:437-438`). Executed releases are logged as "**R1 = 4.8.0 (minor).**".
- **Slices S1..S7**: units of work mapped to releases (`:417-427`); S6 docs, S7 consumer acceptance. "S0" varies by
  plan: the first repro/measurement on the current release (`PHASE_FROM_STEPS_PLAN.md:356` "S0, done (RUN, §2.1): the
  repro measured on 4.6.0", harness `s0/`; `HISTORY_AND_DIAGNOSTICS_MOVE_PLAN` harness `s0/`; `AZURE_DEVOPS_PARITY_PLAN`
  `s0-probe.yml`), or the first patch slice (`CLICKHOUSE_DRIVER_CLIENT_PLAN.md:253` S0 = F10's fix = R1).
- **Claims C1..**, **findings F1..** (numbered in discovery order; added during execution with the release that
  fixed them), **tests T1..** (WARM_UP grouped by slice: T1-T24, T30-T34, T40-T44, T50-T52), **questions Q1..** (each
  with a bold recommendation), **assumptions A1..**.
- **Decisions D<n>** live in `ROADMAP.md` §3 (green light + the plan's questions), taken by the owner; the plan header
  and Log quote the owner ("complete the plan in full").

### 5.5 Harness (`plans/<NAME>_PLAN.harness/`)

`README.md` (version and commit measured at, a Files table, Re-running commands, The acceptance it serves); `s0/` the
repro on the current release; `probe/` + `probe-results-<version>.txt` for a client/driver plan (ClickHouse:
`probe/Probe.csproj` with `-p:KronikolVersion=` to build the same probe on the release, `start-ch.sh` for podman);
`results/` measurement outputs; `r1/`, `r2/`: `red-<previous version>-core.txt` (and `-e2e.txt` etc.) from
`redcheck.py`, `mutate.py`, `mutations.txt`, `s7/` consumer outputs; `wiki/r1_wiki.py` the wiki edit applied after
publication; `breakfastprovider/` consumer scripts (`bp_pin.py`, `runlanes.sh`, compare scripts).

### 5.6 Proofs

- **Red first on the previous tag**, each fact failing "for its own reason" and the run kept: WARM_UP R0 "every new
  fact red on v4.6.0" (`red-4.6.0-*.txt`); when the API is new, the facts run on the previous release's tree with
  compile stubs (R1 "on 4.7.3 with a stub `WarmUpCalls` (marking nothing)", `:626-633`; R2 "two compile stubs",
  `:657-659`); passes are listed and explained as guards. ClickHouse R2: "the facts ran red on a forwarding-only client
  first (39 failures, each for want of a record)" (`:396`).
- **Mutations**, each run once and caught by the fact named: a table in §5 (`WARM_UP_PLAN.md:398-413`;
  `CLICKHOUSE...:235-245`) and `mutations.txt` lines `CAUGHT <mutation> (T#) [filter] failed n, passed m` with the
  unmutated control count at the top.
- **Consumer acceptance** (S7): BreakfastProvider in a scratch clone (never the shared checkout) on locally packed
  packages, plus a **control** on the previous release that must fail (`WARM_UP_PLAN.md:488-494`). For ClickHouse the
  consumer was the issue's reporter (BreakfastProvider uses only ADO.NET there, `:308-309`).
- Before declaring done (`WARM_UP_PLAN.md:486-501`): every fact found in the diff; `release.slnf` built in Release for
  every target; full Playwright suite; the CI run of every pushed SHA read; doc comments re-read by grep; the issue
  closed with a comment naming the releases; a ROADMAP Appendix C row for leftovers; no "fixes #N" in passing and no
  bracketed skip-ci marker. ClickHouse adds "The real-server facts ran in CI, not skipped: read the job's log for them"
  (`:305`).

### 5.7 Test list format

`| # | Fact | Red on <version> because | Slice |` (WARM_UP `:358`), or with a file column
`| # | Fact | File | Red on | Slice |` (ClickHouse `:218`); then `| Mutation | Caught by |`. Facts are written as
behaviour sentences with the issue or finding in brackets ("T3 | One call of a shape: none marked. Two (600, 5): the
first is marked (F1)"); new class names listed in the paragraph above the table (`:353-356`).

### 5.8 Releases, changelog, docs and Kronikol4J hand-off

- **6.2 Releases** table `| Release | Contents | Bump | Why this part moves |`, citing `CLAUDE.md` ("anything new is a
  minor, and the highest-ranking change decides"; "a bug fix that changes observable behaviour is still a patch; call
  it out"). ROADMAP rule 7 (`ROADMAP.md:90-93`): "A new option is a minor, flipping its default is a major."
- **6.3 Changelog drafts** as blockquotes: `> **Patch: <headline>.** ... The patch part moved because nothing is new
  for a consumer to call.` / `> **Minor: <headline> (#113).** ... The minor part moved because ...`. Published
  entries open `**Minor - <headline> (#126).**` then the plan, release and roadmap row (`CHANGELOG.md:474-483`).
- **6.4 Wiki** table `| Page | Change | Release |` with line numbers; note the checkout state ("behind origin: pull
  first, edit in a worktree of its own", `WARM_UP_PLAN.md:463`); edits applied after publication by `wiki/rN_wiki.py`;
  the wiki commit recorded in the Log.
- **6.5 Kronikol4J**: what pins exist (`GoldenHtmlParityTest.java:63`, `ReportDataParityTest.java:36` pin .NET 3.0.43
  HTML and 3.0.47 data, so no Java test fails), and one ledger line per release "after publication", saying mirrored
  or not and why.
- Release mechanics seen in the 4.x commits: bump `<Version>` in `Directory.Build.props:4`; `.claude-plugin/plugin.json:5`
  and `marketplace.json:14` carry the same version (held by `PluginManifestTests`); `templates/*/KronikolComponentTests.csproj`
  pin the **previous** published release (4.8.0 at 4.9.0); `templates/github-actions/kronikol-history/VERSION` names the
  new `Kronikol.Tool` version (4.9.0). Tag `v{version}`, push; then nuget.org lists all 62 ids.

### 5.9 The Log section

Dated bullets (`WARM_UP_PLAN.md:567-668`): "**2026-10-06.** Written at 4.6.0 (`37e93813`)" with what was read and run
(nested bullets); "**2026-10-07.** Green-lit by the owner ("...")" with the worktrees used; per release
"**R0 = 4.7.3 (patch).**" what shipped and what was found, "Proof (harness `r0/`): ...", then "**4.7.3 published.**
(commit, tag `v4.7.3`, templates pinned to 4.7.2): CI <run id>, Release <run id> and CodeQL <run id> passed; nuget.org
lists all 62 ids; the wiki has R0's edits (`0bbae05`) and Kronikol4J's ledger its line (`959cca3`)."

## 6. `plans/ROADMAP.md` (1,023 lines)

- **Header** (`:3`) accumulates amendments: "**Amended 2026-10-06:** #113 placed as 1.19 (stage 1, rule 1), with
  decision D33 and `WARM_UP_PLAN.md`; nothing else moved." and later "**Amended 2026-10-07:** #130 placed as 1.20
  (stage 1, rule 1), green-lit in full by the owner as decision D35, and shipped as 4.7.1 and 4.7.2; nothing else moved."
- **Rules** `:70-104` (rule 1 "What is shipped and wrong comes before what is new"; rule 5 same files in sequence;
  rule 7 options minor, default flips major).
- **§3 decision queue** `:137-180`, table `| # | Decision | Recommendation, and the reason | Blocks |` (`:143`).
  **Highest: D36** (`:180`). Open form (stale diff in `C:/Code/Kronikol`):
  `| D33 | **#113, first-call warm-up in durations** (`WARM_UP_PLAN.md`, row 1.19): the green light, and the plan's Q1
  to Q15. Q5, the look of the badge's marker, is the one only the owner can answer; the rest carry a measured
  recommendation. **Open** | Green-light R0 now and alone: a patch for live defects (...). For R1, take the plan's
  recommendations: ... | 1.19 |`. Taken form (origin/main `:177`): "... **Taken 2026-10-07 by the owner**, who asked
  for the plan to be implemented in full: Q5 as recommended (A, ...), every other question as recommended ...".
- **Stage 1, "The patch train"** `:224-251`, table `| # | Item | Source | Notes |` (`:230`), rows `1.1` (`:232`) to
  **`1.20` (`:251`), the highest**. Row form, e.g. 1.16 (`:247`): `| 1.16 | **Done: 4.4.1 (R1) and 4.5.0 (R2),
  2026-10-01.** **#126** <the defect in one sentence> (`CLICKHOUSE_DRIVER_CLIENT_PLAN.md`). **R1, a patch, first:** ...
  **R2, a minor:** ... Out of reach: ... | #126. RUN: reproduced on 4.4.0 ... (the plan's harness) | Needs D32. Placed
  by rule 1: ... R2 is marked **minor**. Added 2026-10-01 |`. Before execution the row starts with the issue and says
  `(`<PLAN>.md`, **NOT green-lit**, D<n>)`.
- **§7 Coverage check** `:632-655`: `**Issues.**` line (`:636-638`) entries `#126 → 1.16, D32 · #113 → 1.19, D33 · #115 →
  1.17, D36 · #130 → 1.20, D35` (append `#135 → 1.2x, D3x`); `**Plans.**` line (`:640-647`) entries
  `` `CLICKHOUSE_DRIVER_CLIENT` → 1.16, D32 `` (append `` `<NAME>` → 1.2x, D3x ``).
- **Appendix B.1 register** `:776-816`, table `| Plan | Status | Shipped | Stage | What the row records |`, newest first.
  Open form: `| `WARM_UP_PLAN` (2026-10-06) | NOT green-lit; D33 open | none | 1.19, D33 | #113: <one paragraph> |`.
  Done form (`:782`): `| `CLICKHOUSE_DRIVER_CLIENT_PLAN` (2026-10-01) | DONE; D32 taken 2026-10-01 by the owner, as
  recommended; R1 and R2 shipped | none | 1.16, D32 | #126: ... |`. B.2 executed `:817`; **Appendix C** leftovers `:920`.
- **Collision:** #134 (gRPC) is being planned in parallel (`C:/Code/Kronikol-grpc134`). The WARM_UP log records the
  practice: "Placed as row 1.19 and D33 ..., after rows 1.17 (#115) and 1.18 (#105), written the same day by other
  sessions; numbers agreed with them before writing" (`WARM_UP_PLAN.md:581-582`).

## 7. Versions

`Directory.Build.props:4` `<Version>4.9.0</Version>`; `:5` `<TargetFramework>net10.0</TargetFramework>` (what test
projects get); `:6` `<MultiTargetFrameworks>net8.0;net9.0;net10.0</MultiTargetFrameworks>` (packages opt in with
`<TargetFramework />` + `<TargetFrameworks>$(MultiTargetFrameworks)</TargetFrameworks>`). One `<Version>` for every
package (62 ids on nuget.org per release); no per-project versions; no `Directory.Packages.props` (each csproj floats
its client, e.g. `Confluent.Kafka 2.*`, `Lib.Harmony 2.*`, ROADMAP 1.11/#97). The next release after 4.9.0 is 4.9.1 for
a patch or 4.10.0 for a minor (`CLAUDE.md`).

Bump precedents for #135's parts: adding a field Kafka entries lacked was a **patch** in 3.15.1 (timestamps, "Report
output changes" called out); a missed capture surface that a new public type fixes was a **minor** (4.5.0); a new
option is a minor even with today's default (`CLAUDE.md`, ROADMAP rule 7). So: the shared-code defect and (arguably)
`DependentProducerBuilder` interception as patches (the wiki promises "any call to `.Build()`"), Activity ids on Kafka
entries a patch with the export/trace behaviour change called out, the JSON envelope option a minor (opt-in, default
unchanged). The owner decides; the plan states which part moves and why.

## 8. Suggested red-first facts for #135 (from the above; for the planner)

| Fact | Red on 4.9.0 because | Project |
|---|---|---|
| With `<string,string>` enabled, `ProducerBuilder<Null,string>` and `ConsumerBuilder<Null,string>` build working clients (type, a `Produce`/`Subscribe` that does not throw) | `EntryPointNotFoundException` (RUN) | BuildInterception |
| Two reference pairs enabled (`<string,string>`, `<string,byte[]>`) each get their own tracked type | last patch wins (RUN) | BuildInterception |
| After `Reset()`, a `<Null,string>` builder works | stays broken (RUN) | BuildInterception |
| `DependentProducerBuilder<string,string>(handle).Build()` is tracked when `<string,string>` is enabled; `<Null,string>` dependent builder unharmed; value-type pairs (`<int,string>`) too | raw producer (RUN) | BuildInterception (real producer for the handle, no broker) |
| The facts above on net8.0 as well as net10.0 (multi-target the BuildInterception tests) | runs on net10.0 only | csproj |
| Produce under an ambient W3C `Activity`: both entries carry its trace id (32 hex) and span id; with no ambient activity, the chosen rule (own span? random ids?) | `null` (RUN) | Kafka |
| Consume: the entry carries the processing activity's ids; a `traceparent` header (if the design injects one) round-trips producer → consumer | `null` | Kafka |
| Sync `Produce`: the delivery handler logs once with partition and offset (fake that invokes the handler) | never exercised (G2) | Kafka |
| JSON envelope option: content is `{"key":...,"value":...}`, value embedded as JSON when it parses, as a string otherwise; key/value toggles respected; Summarised logs nothing; default unchanged (`Key: k, Value: v`) | no option | Kafka |
| `kronikol query values --path '$.value.x' --request` reaches a Kafka payload logged with the envelope | non-JSON content | Kronikol.Tests (query) |
| Raw label for Commit/Flush/Unsubscribe has no trailing space (G8), if fixed on the way | `"Commit "` | Kafka |
| Real broker lane (Testcontainers, `ClickHouseServerFixture` pattern): produce and consume through tracked clients, identity headers and trace ids across the broker | no broker test | new class or project |

Probe sources and outputs to copy into a harness: `.../scratchpad/probe-shipped/`, `probe-harmony/` to `probe-harmony6/`
(`Program.cs`, `Probe.csproj`, `results.txt`).
