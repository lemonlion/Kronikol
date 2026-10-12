# Confluent.Kafka internals for lemonlion/Kronikol#135

Researched 2026-10-07 for the plan to fix #135 ("build interception misses DependentProducerBuilder, and
produce/consume entries carry no trace ids"). No repository file was edited. Probes ran outside the repo,
under this scratchpad (`probe-dependent/`, `probe-kronikol/`).

Marks: **VERIFIED** means read in source (path and line given), confirmed by reflection, or run (command
and output given). **UNVERIFIED** means inference or documentation not checked line by line.

Abbreviations: `CK` = `https://github.com/confluentinc/confluent-kafka-dotnet`, paths under
`src/Confluent.Kafka/` at tag `v2.15.1` unless another tag is named. Local copies of the trees are in
`scratchpad/ck-src/full-v2.15.1`, `full-v2.16.0-RC1` and `full-v2.0.2` (from codeload tarballs).

---

## 0. Headline findings

1. **DependentProducerBuilder<TKey,TValue> is a standalone class.** It derives from `System.Object`,
   not `ProducerBuilder`, and is not sealed. Its `public virtual IProducer<TKey,TValue> Build()` does
   `new Producer<TKey,TValue>(this)` through an internal constructor. It returns the same internal
   `Producer<,>` type that `ProducerBuilder.Build()` returns. Neither builder routes through the other.
   It is byte-identical from v1.0.0 to v2.15.1. 2.16.0-RC1 only adds experimental serializer-builder
   setters. VERIFIED (§1, §2).
2. **Delivery handlers never run in the producer caller's context.** `Produce(..., handler)` handlers,
   for owned and dependent producers alike, run on the owning producer's poll thread (a dedicated
   ".NET Long Running Task" thread). They carry the ExecutionContext captured when the **owner's**
   `ProducerBuilder.Build()` ran: its AsyncLocals and its `Activity.Current`, which is often a stopped
   Activity. `ProduceAsync` continuations do get the caller's context back. VERIFIED (§3, probe).
3. **Confluent.Kafka has no tracing of its own.** 2.15.1 and 2.16.0-RC1 contain no Activity,
   ActivitySource, DiagnosticSource or traceparent code. There are two instrumentation families:
   - OpenTelemetry.Instrumentation.ConfluentKafka (alpha). Spans `send {topic}`, `poll {topic}` and
     `process {topic}`. W3C propagation. Consumer spans link to the producer span rather than taking
     it as parent.
   - Confluent.Kafka.Extensions.Diagnostics (vhatsura). Spans `{topic} publish` and `{topic} process`.
     Writes `traceparent`/`tracestate` itself. The consumer span is a child of the producer span. It
     also instruments `DependentProducerBuilder`.

   VERIFIED (§4).
4. **The Harmony approach Kronikol ships (4.9.0) is broken for reference-type pairs**, and a
   `DependentProducerBuilder` patch done the same way inherits every defect. VERIFIED by running the
   shipped packages (§5, §6):
   - **Patching one instantiation hijacks all the others.** Patching `<string,string>` reroutes every
     reference-type instantiation, for example `<Null,string>`, `<string,byte[]>` and
     `<byte[],byte[]>`. Their Build() then returns a `Producer<string,string>`, wrapped in a
     `TrackingKafkaProducer<string,string>`. That object does not implement the caller's interface,
     and the first call throws `System.EntryPointNotFoundException`.
   - **With two reference-type pairs, the last one patched wins** for all of them.
   - **Reset() does not repair it.** `KafkaBuildInterceptor.Reset()` / `UnpatchAll` leaves the method
     detoured to an empty replacement closed over the last pair patched, so the other pairs stay
     broken for the life of the process.
   - **Tier-up silently ends interception.** After tiered compilation promotes the shared
     `Build()`, interception stops. The shipped `ProducerBuilder<string,string>` patch stopped
     wrapping after 151 builds on .NET 8, and stays stopped. Non-generic methods and value-type
     instantiations do not have this problem.
5. **Existing Kronikol bugs found on the way.** VERIFIED against 4.9.0 (§6):
   - `TrackingKafkaProducer.Produce(handler)` attributes its entry to the test whose context built the
     producer, not the test that produced.
   - `Produce` on a tracked producer configured with `EnableDeliveryReports=false` throws
     `InvalidOperationException`.
   - A failed `ProduceAsync` logs nothing.
6. **A non-generic choke point exists.** Every produce from every producer (owned or dependent, any
   type pair) calls the internal non-generic `Confluent.Kafka.Impl.SafeKafkaHandle.Produce(...)`
   synchronously, on the producing caller's thread. The caller's `Activity.Current` and AsyncLocals
   are in place there. A Harmony prefix on it fired for every pair and survived tier-up (2000 of 2000
   calls). VERIFIED (§5.8). It is an internal API, though: its header parameter type changed between
   2.0.2 and 2.15.1.

---

## 1. Versions in play

| Item | Value | Status / source |
|---|---|---|
| Confluent.Kafka the repo resolves | **2.15.1** (librdkafka.redist 2.15.1) | VERIFIED: `C:/Code/Kronikol/tests/Kronikol.Tests.Kafka/obj/project.assets.json` and `.../Kronikol.Tests.Kafka.BuildInterception/obj/project.assets.json` (grep `"Confluent.Kafka/2.15.1"`). The worktree `C:/Code/Kronikol-kafka135` has not been restored (no obj/). |
| Lib.Harmony the repo resolves | **2.4.2** | VERIFIED: same assets file. |
| Version ranges in the repo | `Confluent.Kafka` `2.*` (`src/Kronikol.Extensions.Kafka/Kronikol.Extensions.Kafka.csproj`), `Lib.Harmony` `2.*` (`src/Kronikol.Extensions.Kafka.BuildInterception/...csproj`) | VERIFIED (read). |
| What consumers of the package actually get | The **published** `Kronikol.Extensions.Kafka` 4.9.0 nuspec declares `Confluent.Kafka` `version="2.15.1"` (meaning >= 2.15.1) for net8.0, net9.0 and net10.0. BuildInterception 4.9.0 declares `Lib.Harmony` `2.4.2`. Pack wrote the resolved version, not `2.0.0`. So the effective minimum Confluent.Kafka for a Kronikol consumer is the one Kronikol was restored against at release time. | VERIFIED: `https://api.nuget.org/v3-flatcontainer/kronikol.extensions.kafka/4.9.0/kronikol.extensions.kafka.nuspec` and the `.buildinterception` one. |
| Confluent.Kafka 2.x on nuget.org | First 2.x is **2.0.2** (2.0.0 and 2.0.1 were never published). Latest stable 2.15.1. `v2.16.0-RC1` exists as a git tag only and is not on nuget.org. | VERIFIED: `https://api.nuget.org/v3-flatcontainer/confluent.kafka/index.json`; `gh api repos/confluentinc/confluent-kafka-dotnet/tags`. |
| Confluent.Kafka 2.15.1 TFMs | net8.0, net10.0, netstandard2.0, net462 | VERIFIED: `Confluent.Kafka.csproj` L19 plus `src/Directory.Build.props` L4; local `~/.nuget/packages/confluent.kafka/2.15.1/lib/`. Kronikol multi-targets net8.0;net9.0;net10.0 (`Directory.Build.props` L6), so net9.0 binds to Confluent's net8.0 asset (UNVERIFIED, standard NuGet nearest-TFM rule). |
| Probe runtime | .NET 10.0.11 and .NET 8.0.27, SDK 10.0.300, Windows 11 | VERIFIED (probe header lines). |

---

## 2. Q1: DependentProducerBuilder<TKey,TValue>, the other builders, and other routes to IProducer/IConsumer

### 2.1 Class shape (v2.15.1, `DependentProducerBuilder.cs`), VERIFIED by source and reflection

- `public class DependentProducerBuilder<TKey, TValue>` (L30). BaseType `System.Object`, IsSealed False,
  IsPublic True (reflection, probe `types`). Doc says "[API-SUBJECT-TO-CHANGE] - This class may be
  removed in the future" (L27-28).
- Constructor: `public DependentProducerBuilder(Handle handle)` (L63-66). The only constructor.
- Public read/write properties: `Handle Handle` (L35), `ISerializer<TKey> KeySerializer` (L40),
  `ISerializer<TValue> ValueSerializer` (L45), `IAsyncSerializer<TKey> AsyncKeySerializer` (L50),
  `IAsyncSerializer<TValue> AsyncValueSerializer` (L55). They are **public get/set**, where
  ProducerBuilder's equivalents are `internal protected` (`ProducerBuilder.cs` L100-115).
- Fluent setters, each returning `DependentProducerBuilder<TKey,TValue>` and non-virtual:
  - `SetKeySerializer(ISerializer<TKey>)` (L71)
  - `SetValueSerializer(ISerializer<TValue>)` (L80)
  - `SetKeySerializer(IAsyncSerializer<TKey>)` (L89)
  - `SetValueSerializer(IAsyncSerializer<TValue>)` (L98)

  Unlike ProducerBuilder's setters (L303-368), they do **not** throw when a serializer is set twice.
- `public virtual IProducer<TKey, TValue> Build() { return new Producer<TKey, TValue>(this); }`
  (L107-110). Reflection: `virtual=True final=False`.
- No error, log, statistics or OAuth handlers and no config. Those belong to the owner producer.

### 2.2 What Build() does, VERIFIED (`Producer.cs`)

`Producer<TKey,TValue>` is `internal class` (L33). Its constructors are internal (reflection:
`ctor (internal): Void .ctor(DependentProducerBuilder`2)` and `... .ctor(ProducerBuilder`2)`).

`internal Producer(DependentProducerBuilder<TKey,TValue> builder)` (L556-568):
- `this.borrowedHandle = builder.Handle;`
- `if (!borrowedHandle.Owner.GetType().Name.Contains("Producer")) throw new Exception("A Producer instance may only be constructed using the handle of another Producer instance.");`
  (L560-563). The probe confirmed the throw for a consumer's handle.
- `InitializeSerializers(...)`. Default serializers come from a static map for Null, int, long,
  string, float, double and byte[] (L51-60, L503-554).
- **No** native handle, **no** config parsing, **no** callbacks registered, **no** poll task. The fields
  keep their defaults: `manualPoll=false` (L66) and `enableDeliveryReports=true` (L67).
- `Dispose` is a no-op for a borrowed handle: `if (ownedKafkaHandle == null) { return; }` (L433).
  Probe: disposing a dependent leaves the owner usable.
- `Handle` getter: an owner returns `new Handle { Owner = this, LibrdkafkaHandle = ownedKafkaHandle }`
  (a new object each call). A dependent returns the borrowed Handle object itself (L490-501). Probe:
  `dep.Handle same as the one passed? True`, and the owner's Handle is not reference-equal across calls.
- The dependent's TKey/TValue may differ from the owner's. Probe: from a `<string,string>` owner,
  dependents `<string,byte[]>` and `<Null,string>` were built and produced fine. A dependent of a
  dependent also works.
- **Decorated owners are fine.** A dependent built from a *decorated* owner's handle works, because
  `Handle.Owner` is the inner `Producer<,>` (Kronikol's `TrackingKafkaProducer.Handle` delegates to
  `_inner.Handle`). Probe: "DependentProducerBuilder<string,string>(decoratedOwner.Handle).Build() ->
  Confluent.Kafka.Producer`2[...]".

Runtime type of every built producer, VERIFIED (probe `types`): `Confluent.Kafka.Producer`2[K,V]`
for both builders. A dependent producer is indistinguishable by type from an owned one.

### 2.3 Does one builder route through the other? No. VERIFIED

`ProducerBuilder.Build()` → `new Producer<TKey,TValue>(this)` (`ProducerBuilder.cs` L373-376) → the
internal `Producer(ProducerBuilder<,>)` constructor (`Producer.cs` L570-747).
`DependentProducerBuilder.Build()` → the internal `Producer(DependentProducerBuilder<,>)` constructor
(L556). Neither builder references the other type.

### 2.4 Every public builder in Confluent.Kafka 2.15.1. VERIFIED by reflection over exported types plus source grep

| Builder | Build() returns | Source |
|---|---|---|
| `ProducerBuilder<TKey,TValue>` | `IProducer<TKey,TValue>` (virtual) | `ProducerBuilder.cs` L60, L373 |
| `DependentProducerBuilder<TKey,TValue>` | `IProducer<TKey,TValue>` (virtual) | `DependentProducerBuilder.cs` L30, L107 |
| `ConsumerBuilder<TKey,TValue>` | `IConsumer<TKey,TValue>` (virtual) | `ConsumerBuilder.cs` L27, L589-592 |
| `AdminClientBuilder` | `IAdminClient` (virtual) | `AdminClientBuilder.cs` L26, L173 |
| `DependentAdminClientBuilder` | `IAdminClient` (virtual) | `DependentAdminClientBuilder.cs` L27, L45 |

There is **no `DependentConsumerBuilder`**. Only ProducerBuilder and DependentProducerBuilder yield
IProducer. Only ConsumerBuilder yields IConsumer.

### 2.5 Any other public way to get an IProducer/IConsumer? No, within Confluent.Kafka. VERIFIED

- No exported non-interface type implements `IProducer<,>`, `IConsumer<,>` or `IAdminClient`. The
  probe's reflection listing was empty.
- `grep -rn 'new Producer<\|new Consumer<\|new AdminClient('` over all of `CK/src` at v2.15.1 finds
  only the five Build methods and the builders' `Config` helpers. The construction sites are
  `ProducerBuilder.cs:375`, `DependentProducerBuilder.cs:109`, `ConsumerBuilder.cs:591`,
  `AdminClientBuilder.cs:175` and `DependentAdminClientBuilder.cs:47`.
- `InternalsVisibleTo` goes only to `Confluent.Kafka.UnitTests`, `Confluent.Kafka.OAuthBearer.Aws`
  and `Confluent.Kafka.OAuthBearer.Aws.UnitTests` (`AssemblyInfo.cs` L21, L27, L33).
- Other routes exist only outside the library:
  - reflection on the internal constructors (used in the probe's dispatch prefix, §5.6);
  - third-party `IProducer` implementations or decorators, which wrap a builder-built producer;
  - builder **subclasses** that override the virtual `Build()`. For example OpenTelemetry's
    `InstrumentedProducerBuilder` and `InstrumentedConsumerBuilder` call `base.Build()` (§4.2), so a
    patch on the base method still fires. An override that never calls base cannot produce a real
    Confluent producer except through another builder.

### 2.6 Who uses DependentProducerBuilder (context, VERIFIED by GitHub code search 2026-10-07)

- **Confluent's examples:**
  - `examples/Web/KafkaClientHandle.cs` L45 builds a shared `ProducerBuilder<byte[], byte[]>`.
    `KafkaDependentProducer.cs` L34 builds `new DependentProducerBuilder<K, V>(handle.Handle).Build()`,
    registered as `KafkaDependentProducer<Null, string>` and `<string, long>` (`Startup.cs` L39-40).
  - `examples/MultiProducer/Program.cs` L36-37: owner `<string,string>`, dependent `<Null,int>`.
- **Libraries and projects** (from `gh search code "DependentProducerBuilder" --language C#`): Azure
  Functions Kafka extension (`src/Microsoft.Azure.WebJobs.Extensions.Kafka/Output/KafkaProducer.cs`),
  Eventuous (`src/Kafka/src/Eventuous.Kafka/Producers/KafkaBasicProducer.cs`), Squidex
  (`messaging/Squidex.Messaging.Kafka/KafkaTransport.cs`), epam-kafka, eventso/kafka-producer,
  Rebus.Kafka, kmcclellan/kafka-di (`Confluent.Kafka.DependencyInjection/.../GlobalProducer.cs`),
  kafka-dataflow, and vhatsura's Diagnostics (`DependentProducerBuilderExtensions.cs`). Many app
  repos copy the `KafkaDependentProducer<K,V>` example.
- **Implication:** reference-type pairs such as `<Null,string>`, `<string,string>` and
  `<byte[],byte[]>` dominate, which is where the shared-generic Harmony defects in §5 bite.

---

## 3. Q2: when DependentProducerBuilder appeared, and API stability across 2.x

- **Introduced** in commit `fcc92f088d` (2019-02-04, "use the builder pattern to construct clients
  (#716)"). The first tag carrying the file is `v1.0-beta3` (annotated 2019-02-07; raw URL returns 200).
  It was in the first stable 1.0.0. VERIFIED: `gh api 'repos/confluentinc/confluent-kafka-dotnet/commits?path=src/Confluent.Kafka/DependentProducerBuilder.cs'`
  lists fcc92f088d (2019-02-04), 74bef73234 (2019-02-13, "builder classes should return interfaces"),
  8bb0930a66 (2019-04-08 serde), 5516eba507 (2019-04-18 sync over async), 76f38fd6f6 (2019-04-18 "make
  client classes internal"), 224b562c9f (2019-04-23 API-SUBJECT-TO-CHANGE notice), and then nothing
  until **4239a5449a (2026-10-06)**.
- **Identical v1.0.0 → v2.0.2 → v2.15.1.** `diff -u` of the file at v1.0.0 and at v2.0.2 against
  v2.15.1 shows no difference. VERIFIED. Every Confluent.Kafka 2.x on nuget.org (2.0.2 to 2.15.1) has
  exactly the API in §2.1.
- **Producer internals the plan relies on are the same in 2.0.2.** VERIFIED (grep of
  `full-v2.0.2/src/Confluent.Kafka/Producer.cs`):
  - `StartPollTask` uses LongRunning (L90-116);
  - the dependent constructor and its Owner-name check (L551-555);
  - the "delivery handler specified but reports disabled" check (L865);
  - `TypedTaskDeliveryHandlerShim` with `RunContinuationsAsynchronously` (L938-941).
- **2.16.0-RC1** (tag already contains 4239a5449a; `gh api .../compare/v2.16.0-RC1...4239a5449a` →
  `behind_by=1`). VERIFIED by diff `full-v2.15.1` vs `full-v2.16.0-RC1`:
  - DependentProducerBuilder adds four public properties marked "**EXPERIMENTAL**": `KeySerializerBuilder`,
    `ValueSerializerBuilder` (`ISerializerBuilder<T>`), `AsyncKeySerializerBuilder` and
    `AsyncValueSerializerBuilder` (`IAsyncSerializerBuilder<T>`).
  - It adds four setters, `SetKeySerializerBuilder` and `SetValueSerializerBuilder` (sync and async
    overloads), and private mutual-exclusion checks.
  - `Build()` is unchanged: `public virtual IProducer<TKey,TValue> Build()` → `new Producer<TKey,TValue>(this)`.
  - `Handle` gains an `internal IEnumerable<KeyValuePair<string,string>> Config`.
  - The dependent constructor (RC1 `Producer.cs` L661-690) also calls `PropagateClusterId()`.
  - The threading model is unchanged: `StartPollTask` LongRunning (RC1 L107-133) and
    `RunContinuationsAsynchronously` (RC1 L1104).
  - Because `Kronikol.Extensions.Kafka` floats on `2.*`, the next restore after 2.16.0 ships will pick
    it up. The change is additive for anything Kronikol would touch.
- **Plan note:** a Harmony target must not be looked up by exact property set. Build() itself is
  stable. UNVERIFIED for any version after 2.16.0-RC1.

---

## 4. Q3: producer threading and ExecutionContext

### 4.1 How the poll loop starts. VERIFIED (source)

- **Owned producer** (`ProducerBuilder` path): the constructor ends with
  `if (!manualPoll) { callbackCts = new CancellationTokenSource(); callbackTask = StartPollTask(callbackCts.Token); }`
  (`Producer.cs` L738-742). `StartPollTask` (L90-117) is
  `Task.Factory.StartNew(() => { while (true) { ... ownedKafkaHandle.Poll((IntPtr)cancellationDelayMaxMs) ... } }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default)`.
  `manualPoll` is true only if `enable.background.poll=false` (L612-616).
- **Dependent producer:** no poll task, nothing registered (L556-568).
- **LongRunning gets a dedicated thread** named ".NET Long Running Task", IsBackground=true (.NET
  `ThreadPoolTaskScheduler.cs` L45-52, release/8.0). **The Task captures the ExecutionContext of the
  thread that calls StartNew**: `CapturedContext = ExecutionContext.Capture()` (`Task.cs` L526), and
  the body runs under `ExecutionContext.RunInternal(ec, ...)` (`Task.cs` L2334-2345). VERIFIED in
  dotnet/runtime source and observed: probe thread name is ".NET Long Running Task", pool=False,
  background=True.
- **So the whole poll loop, and every callback it serves, runs inside a snapshot of the context of the
  code that called the owner's Build()**, for the producer's lifetime. Those callbacks are delivery
  reports, error, statistics and OAuth. `Activity.Current` is an `AsyncLocal<Activity?>`
  (`System.Diagnostics.DiagnosticSource/.../Activity.Current.net46.cs` L10, release/8.0).
  `Activity.Stop()` only resets Current in the stopping flow (`Activity.cs` L743-763, `SetCurrent(_previousActiveActivity)` at L761),
  so the poll thread keeps seeing a stopped Activity. VERIFIED (probe).

### 4.2 Which thread runs delivery reports, including a dependent's

- The `dr_msg_cb` is registered on the owner's config (`conf_set_dr_msg_cb(configPtr, DeliveryReportCallback)`
  L664-667, with `DeliveryReportCallback = DeliveryReportCallbackImpl` L591). librdkafka invokes it from
  `rd_kafka_poll` on the shared handle. `ProduceImpl` passes the per-message `IDeliveryHandler` (a shim)
  as a GCHandle opaque (L299-324). The owner's `DeliveryReportCallbackImpl` (L200-279) recovers it and
  calls `deliveryHandler.HandleDeliveryReport(...)` (L262). VERIFIED (source).
- **A dependent producer's delivery reports run on the OWNER's poll thread, in the OWNER's Build()
  context.** VERIFIED (probe `threading`, .NET 10.0.11 and 8.0.27, identical). Excerpt, .NET 10:
  ```
  [owner.Produce(handler)] caller: thread=2 ... marker='C: caller of owner.Produce(handler)' Activity.Current=C-caller (stopped=False, trace=e2f3...)
  [owner.Produce(handler)] handler: thread=4 name='.NET Long Running Task' pool=False background=True marker='A: owner's Build() caller' Activity.Current=A-owner-build (stopped=True, trace=6fd5...) error=Local_MsgTimedOut
  [dependent.Produce(handler)] caller: thread=6 ... marker='D: caller of dependent.Produce(handler)' Activity.Current=D-caller (stopped=False, trace=f057...)
  [dependent.Produce(handler)] handler: thread=4 name='.NET Long Running Task' ... marker='A: owner's Build() caller' Activity.Current=A-owner-build (stopped=True, trace=6fd5...) error=Local_MsgTimedOut
  ```
  Context "B" (the dependent's own Build() caller) is never seen.
- **Flush and Poll can also serve callbacks on the calling thread.**
  - VERIFIED (source): librdkafka `rd_kafka_flush` in callback mode calls `rd_kafka_poll(rk, tmout)` in
    a loop on the calling thread (`confluentinc/librdkafka` v2.15.1 `src/rdkafka.c` L6445-6461).
    `Producer.Flush` calls `KafkaHandle.Flush` (`Producer.cs` L367-403), and a dependent's Flush uses
    the shared handle.
  - Observed in the probe: all 30 handlers in 3 `Flush()` rounds ran on the poll thread, none on the
    Flush caller (probe `flush`). The poll thread usually wins the race. Either way, it is not the
    Produce caller's context.
  - With `enable.background.poll=false` on the owner, callbacks run wherever someone calls the owner's
    `Poll()`/`Flush()`. VERIFIED (source, L345-350).
  - A dependent's `Poll()` never polls the handle: its `manualPoll` is false, so it waits on its own
    `pollSyncObj`, which nothing pulses (L352-362). VERIFIED (source). Not probed.
- **Delivery reports need delivery reports enabled on the owner, not the dependent.** A dependent's
  `enableDeliveryReports` is always true (never parsed). If the owner was built with
  `enable.delivery.reports=false`, no `dr_msg_cb` is registered, so a dependent's `ProduceAsync` task
  would never complete and its GCHandle would leak. UNVERIFIED (inferred from L299-324 and L664-667;
  not probed).
- **Owner built under `ExecutionContext.SuppressFlow()`:** the handler sees no AsyncLocals and
  `Activity.Current=<null>`. VERIFIED (probe, "H").

### 4.3 Produce(topic, message, deliveryHandler)

- Called on the caller's thread: serialization, then `ProduceImpl` (`Producer.cs` L869-945). The
  handler is wrapped in `TypedDeliveryHandlerShim_Action` (L927-933, L1002-1050) and invoked as
  `Handler(dr)` (L1045-1048) **on the owner's poll thread, as in §4.2**.
- **`Activity.Current` in the handler is not the producer caller's.** It is whatever Activity was
  current when the owner's Build() ran (stopped by now), or null. VERIFIED (probe).
- `if (deliveryHandler != null && !enableDeliveryReports) throw new InvalidOperationException("A delivery handler was specified, but delivery reports are disabled.");`
  (L874-877). VERIFIED (source and probe §6.3).
- Exceptions thrown by a delivery handler are caught and stored in `handlerException`, then surfaced
  later through the error handler as `Local_Application` (L275-278, L99-103). VERIFIED (source).

### 4.4 ProduceAsync

- **Completion:** `TypedTaskDeliveryHandlerShim : TaskCompletionSource<DeliveryResult<TKey,TValue>>`
  is constructed with `TaskCreationOptions.RunContinuationsAsynchronously` (L947-950). The poll thread
  calls `TrySetResult`/`TrySetException` (L965-998). `ProduceAsync` is an `async` method that does
  `return await handler.Task.ConfigureAwait(false);` (L818). VERIFIED (source).
- **Context after the await is the caller's own:** its AsyncLocals and the very same `Activity.Current`
  object (`sameActivityAsCaller=True`), on a thread-pool thread. The ExecutionContext flows through
  the async state machine, and `ConfigureAwait(false)` affects only the SynchronizationContext.
  VERIFIED (probe, owner and dependent, both runtimes):
  ```
  [dependent.ProduceAsync] after await: thread=6 name='.NET TP Worker' pool=True ... marker='F: caller of dependent.ProduceAsync' Activity.Current=F-caller (stopped=False, ...) sameActivityAsCaller=True error=Local_MsgTimedOut
  ```
- **A failed ProduceAsync throws `ProduceException<,>`** after the delivery report, here
  Local_MsgTimedOut. Kronikol's wrapper then logs nothing (§6.4).

### 4.5 Consumer, for completeness

`Consume(...)` runs `kafkaHandle.ConsumerPoll(...)` synchronously on the caller's thread
(`Consumer.cs` L795-797). There is no background thread in `Consumer.cs`: grep finds no
`Task.Factory`/`new Thread`. Rebalance and other callbacks are served inside that poll. VERIFIED
(source). Kronikol's `TrackingKafkaConsumer` logs right after `_inner.Consume(...)` returns, on the
consume loop's thread (`src/Kronikol.Extensions.Kafka/TrackingKafkaConsumer.cs` L24-41, L70-80).
VERIFIED (read).

---

## 5. Q5: Harmony, and what the probe showed

Probe: `scratchpad/probe-dependent/` (net8.0;net10.0, Confluent.Kafka 2.15.1, Lib.Harmony 2.4.2). It
builds real producers with `BootstrapServers=localhost:1`: no broker is needed to Build, and with
`MessageTimeoutMs=1000` every message yields a real delivery report with `Local_MsgTimedOut`. Each
experiment ran in its own process. The patch code mirrors `KafkaBuildInterceptor` exactly: a
**closed-type** original (`typeof(X<string,string>).GetMethod("Build")`) and a **generic postfix
closed with MakeGenericMethod** (`src/Kronikol.Extensions.Kafka.BuildInterception/KafkaBuildInterceptor.cs`
L123-143, L145-153).

### 5.1 Can DependentProducerBuilder<K,V>.Build() be patched the way ProducerBuilder is? Yes, mechanically. VERIFIED

`harmony.Patch(typeof(DependentProducerBuilder<string,string>).GetMethod("Build"), postfix: ...)` works,
and the postfix fires with `__instance=DependentProducerBuilder<string,string>` and
`__result=Producer<string,string>`. The wrapped result works.
`Harmony.GetPatchInfo(...<string,string>.Build)` reports 1 postfix, and `...<string,byte[]>.Build`
reports null. **That bookkeeping is misleading**, as the next sections show.

### 5.2 Shared generics: one patch reroutes every reference-type instantiation. VERIFIED (both runtimes)

After patching only `DependentProducerBuilder<string,string>.Build()` (probe `harmony-dependent`):
```
postfix<String,String> fired: __instance=DependentProducerBuilder`2[String,Byte[]] __result=Producer`2[String,String]
Dependent<string,byte[]>: runtime type=WrappedProducer`2[String,String] postfixFired=True isinst IProducer<String,Byte[]>=False
  Produce(...) through IProducer<String,Byte[]> -> System.EntryPointNotFoundException: Entry point was not found.
Dependent<Null,string>:   runtime type=WrappedProducer`2[String,String] ... isinst IProducer<Null,String>=False -> EntryPointNotFoundException
Dependent<byte[],byte[]>: runtime type=WrappedProducer`2[String,String] ... isinst IProducer<Byte[],Byte[]>=False -> EntryPointNotFoundException
Dependent<int,string>:    runtime type=Producer`2[Int32,String] postfixFired=False  -> ok   (value-type arg: separate native code)
Dependent<string,int>:    runtime type=Producer`2[String,Int32] postfixFired=False  -> ok
ProducerBuilder<...>      (not patched) -> all ok
```
The replacement body is closed over the patched pair. It runs `newobj Producer<string,string>` for
**any** instance entering the shared `DependentProducerBuilder<__Canon,__Canon>.Build` code. So the
defect is in the body, before any postfix runs: the original Build is also wrong, not only the wrapping.
This matches Harmony's docs: "expect generic methods and methods of generic classes to be shared
between different types of T ... by patching one method, the method will be patched for all types of T"
(`pardeike/Harmony` `Documentation/articles/patching-edgecases.md` L21-28). It also matches upstream
issues:
- #201: "patch gets called for all types ... If the method is patched twice with two different concrete
  types, only the last patch is called ... the original method is also called with the wrong type".
- #407: "only the last patch is executed", closed WONTFIX. The maintainer added "Using generics **in
  patch methods is a really bad idea**".
- #391: on generics, "Sometimes the patch is called for all T ... Sometimes, it's not called at all".
- MonoMod/MonoMod #187: shared generics support is still open.

The ProducerBuilder and ConsumerBuilder patches **shipped today** behave the same. Probes
`harmony-producer` and `harmony-consumer` patched `<string,string>` only:
- `ProducerBuilder<string,byte[]>` and `<Null,string>` return a wrapped `Producer<string,string>`, and
  Produce throws EntryPointNotFoundException.
- `ConsumerBuilder<Ignore,string>` and `<string,byte[]>` return `Consumer<string,string>`, and
  `Subscribe` throws EntryPointNotFoundException.

### 5.3 Two reference-type pairs: the last patch wins for all. VERIFIED

Probe `harmony-two-pairs` patched `<string,string>`, then `<Null,string>`. After that,
`Dependent<string,string>` returns `WrappedProducer<Null,String>` (`isinst IProducer<String,String>=False`,
EntryPointNotFoundException). Only `<Null,string>` works.

### 5.4 Unpatch does not restore the other instantiations. VERIFIED

Probe `harmony-unpatch-info`: after `harmony.UnpatchAll(id)`, `Harmony.GetPatchInfo` reports
"0 postfixes, owners=[]", and `Harmony.GetAllPatchedMethods()` still lists
`DependentProducerBuilder<String,String>.Build`. `<string,string>` works again, but `<string,byte[]>`,
`<Null,string>` and `<byte[],byte[]>` still return `Producer<string,string>` and throw. The method stays
detoured to an empty replacement closed over the last pair.

### 5.5 Tiered compilation silently ends interception on shared generic code. VERIFIED

Probe `harmony-tierup`: 2000 builds after patching, `Task.Delay(50)` every 50 iterations. Results:

| Runtime | Call style / setting | Wrapped | Bypassed | First bypass |
|---|---|---|---|---|
| .NET 10 | direct (`new X<string,string>(h).Build()` in a NoInlining method) | 301 | 1699 | 301 |
| .NET 10 | direct, `DOTNET_TieredCompilation=0` | 2000 | 0 | n/a |
| .NET 10 | direct, `DOTNET_TieredPGO=0` | 251 | 1749 | 251 |
| .NET 10 | direct, `DOTNET_JitNoInline=1` | 501 | 1499 | 501 |
| .NET 10 | virtual (callvirt on a parameter) | 301 | 1699 | 301 |
| .NET 10 | virtual, TieredCompilation=0 | 2000 | 0 | n/a |
| .NET 10 | delegate (`Delegate.CreateDelegate` over Build, cannot be inlined) | 251 | 1749 | 251 |
| .NET 10 | delegate, TieredCompilation=0 | 2000 | 0 | n/a |
| .NET 10 | ProducerBuilder<string,string> (the shipped path), 300 builds with Dispose | 130 | 170 | 130 |
| .NET 10 | non-generic dispatch prefix of §5.6 on DependentProducerBuilder<string,string> | 201 | 1799 | 201 |
| .NET 8 | direct | 251 | 1749 | 251 |
| .NET 8 | delegate | 251 | 1749 | 251 |
| .NET 8 | ProducerBuilder<string,string>, 300 builds | 116 | 184 | 116 |
| .NET 8 | dispatch prefix | 251 | 1749 | 251 |

Once bypassed, it never recovers (`wrappedAfterFirstBypass=0` in every row). The delegate style bypasses
too, so the cause is **Build itself being re-JIT'd at Tier1**, not inlining at the call site.

**Control** (probe `harmony-tierup-control`, identical on both runtimes), postfix on three methods,
2000 calls each:
```
non-generic PlainControl.Make:                    postfix-bypassed=0
GenControl<string>.Make (shared canonical code):  postfix-bypassed=1849 firstBypassAt=151
GenControl<int>.Make (value-type instantiation):  postfix-bypassed=0
(with DOTNET_TieredCompilation=0: all three 0)
```
So Harmony 2.4.2 re-applies its detour after tier-up for non-shared code but **not for shared
canonical generic code**. That is the case for every Confluent builder instantiated over reference
types. VERIFIED (ran). The internal reason (MonoMod's JIT hook not matching the canonical MethodDesc)
is UNVERIFIED.

Thresholds: the release runtime promotes after `TC_CallCountThreshold=30` calls, with
`TC_CallCountingDelayMs=100` applied "while there is startup-like activity"
(`dotnet/runtime` release/8.0 `src/coreclr/inc/clrconfigvalues.h` L566-581). VERIFIED (source). The call
count belongs to the shared canonical method, so calls from **all** reference-type pairs add up
(UNVERIFIED inference). The bypass point is timing-dependent (151 to 601 in the probe), so test suites
that build many producers or consumers can flake.

Related upstream reports, VERIFIED (exist and say this):
- Harmony #526 (2023, patches stop after some iterations; tiered compilation blamed; disabling it fixed it).
- Harmony #307 (2020, tiered JIT overwrote the patch).
- Harmony #504 (patches applied under the debugger but not from the command line). This suggests a
  Debug build of the test project does not protect, because Confluent.Kafka.dll is a Release assembly
  and tiers normally (UNVERIFIED).

**Mitigation seen:** `DOTNET_TieredCompilation=0` removes the bypass. The MSBuild property
`<TieredCompilation>false</TieredCompilation>` in a test project writes the same setting into
runtimeconfig.json (UNVERIFIED, Microsoft docs). That is a consumer-side switch a library cannot set
for itself.

### 5.6 A candidate fix for the wrong-type defect: one non-generic dispatching prefix. VERIFIED, with the §5.5 caveat

Probe `harmony-dispatch-prefix`. The prefix is `static bool DispatchPrefix(object __instance, ref object __result)`.
It:
1. walks `__instance.GetType()` up to `DependentProducerBuilder<,>`;
2. takes that type's generic args;
3. constructs `Producer<K,V>` through the internal constructor
   `GetConstructor(NonPublic, [DependentProducerBuilder<K,V>]).Invoke([__instance])`;
4. wraps it, assigns `__result`, and returns false, so the closed-over original body never runs.

Patched on `<string,string>` only, it produced correctly typed, working wrappers for `<string,string>`,
`<string,byte[]>`, `<Null,string>` and `<byte[],byte[]>` on both runtimes. Value-type pairs
(`<int,string>`, `<string,int>`) were untouched, so they need their own patch per pair.
Harmony accepts `ref object __result` for a reference-type return ("The type must match the return
type of the original or be assignable from it", `Documentation/articles/patching-injections.md` L13-15),
and the probe confirms it.

**It is still bypassed after tier-up** (table above, first bypass at 201 on .NET 10 and 251 on .NET 8).

### 5.7 Patching the canonical `<__Canon,__Canon>` method directly: not viable. VERIFIED

`typeof(DependentProducerBuilder<,>).MakeGenericType(System.__Canon, System.__Canon).GetMethod("Build")`
resolves, but `harmony.Patch(...)` throws `System.InvalidProgramException: Common Language Runtime
detected an invalid program.` on both runtimes (probe `harmony-canon`).

### 5.8 A non-generic choke point: `Confluent.Kafka.Impl.SafeKafkaHandle.Produce`. VERIFIED (probe), internal API

- `internal sealed class SafeKafkaHandle` (`Impl/SafeKafkaHandle.cs` L88, non-generic) has
  `internal ErrorCode Produce(string topic, byte[] val, int valOffset, int valLength, byte[] key, int keyOffset, int keyLength, int partition, long timestamp, IReadOnlyList<IHeader> headers, IntPtr opaque)`
  (L360-367). `Producer.ProduceImpl` reaches it for every Produce and ProduceAsync of every producer,
  owned or dependent, through `KafkaHandle` (`Producer.cs` L310-334).
- Probe `harmony-handle`: a prefix `(string topic, IReadOnlyList<IHeader> headers)` fired once per
  produce for owner `<string,string>.Produce`, dependent `<string,byte[]>.Produce`, dependent
  `<Null,string>.ProduceAsync` and dependent `<int,string>.Produce`. It always ran **on the producing
  caller's thread, with the caller's AsyncLocal and the caller's live Activity**:
  ```
  dependent<string,byte[]>.Produce: prefix calls +1; seen: topic=probe-topic headers=0 thread=2 ... marker='caller of dependent<string,byte[]>.Produce' Activity.Current=caller-dependent<string,byte[]>.Produce (stopped=False, ...)
  2000 produces through a hot call site after patching: prefix calls=2000      (both runtimes)
  ```
- **Caveats.** VERIFIED where marked:
  - It is internal. Its header parameter changed from `IEnumerable<IHeader>` (2.0.2, L348) to
    `IReadOnlyList<IHeader>` (2.15.1 and 2.16.0-RC1). VERIFIED (source). In which version it changed
    is UNVERIFIED.
  - It sees serialized bytes, not typed key/value.
  - It sees no delivery result (partition/offset).
  - `headers` is `Headers.BackingList` (`Headers.cs` L37). Mutating it from a prefix would mean casting
    to the underlying list (UNVERIFIED as a design).
  - Kronikol would still have to choose test identity and Activity at this instant. That is the right
    instant: §4.

### 5.9 How the existing BuildInterception tests exercise this. VERIFIED (read, `tests/Kronikol.Tests.Kafka.BuildInterception/KafkaBuildInterceptorTests.cs`)

- Each test builds real Confluent clients with `BootstrapServers = "localhost:9092"` (L21-30). No broker
  is needed: Build only creates the librdkafka handle and the poll thread, and the probe confirms this.
  Only one test acts on a client (`consumer.Subscribe`, L190-202). None produces.
- Every producer and consumer test uses `<string,string>`. The only other pair is `ConsumerBuilder<int,string>`
  in `Different_Type_Pairs_Are_Independent` (L162-174). Because `int` is a value type, that pair gets
  separate native code, so the test passes while masking §5.2.
- `Dispose() => KafkaBuildInterceptor.Reset()` (L12). By §5.4, Reset leaves the last patched pair's body
  in place. Because all tests use `<string,string>`, nothing notices.
- About 20 Build() calls in the class: under or near the 30-call tier-up threshold today. Adding tests
  could push it over and make the suite timing-flaky (§5.5). UNVERIFIED, extrapolated.

---

## 6. Defects in the shipped Kronikol 4.9.0, reproduced with the published packages

Probe: `scratchpad/probe-kronikol/`, net8.0, PackageReference `Kronikol.Extensions.Kafka.BuildInterception`
4.9.0. It resolves Kronikol 4.9.0, Kronikol.Extensions.Kafka 4.9.0, Confluent.Kafka 2.15.1 and
Lib.Harmony 2.4.2 (obj/project.assets.json). The assembly informational version is
`4.9.0+417c8e5855adb837099c9bb7f98b3a7606a79fbe`, the worktree's HEAD.

### 6.1 Wrong producer type for every other reference-type pair; Reset does not repair. VERIFIED (`pairs`)

```
-- KafkaBuildInterceptor.EnableProducerTracking<string,string>():
  ProducerBuilder<string,string>.Build() -> TrackingKafkaProducer`2[String,String]  isinst IProducer<String,String>=True   Produce -> ok
  ProducerBuilder<Null,string>.Build()   -> TrackingKafkaProducer`2[String,String]  isinst IProducer<Null,String>=False   Produce -> System.EntryPointNotFoundException
  ProducerBuilder<string,byte[]>.Build() -> TrackingKafkaProducer`2[String,String]  isinst IProducer<String,Byte[]>=False Produce -> System.EntryPointNotFoundException
  ProducerBuilder<int,string>.Build()    -> Producer`2[Int32,String]  ok
-- then EnableProducerTracking<Null,string>() as well:
  ProducerBuilder<string,string>.Build() -> TrackingKafkaProducer`2[Null,String]  isinst IProducer<String,String>=False  Produce -> EntryPointNotFoundException
  ProducerBuilder<Null,string>.Build()   -> TrackingKafkaProducer`2[Null,String]  ok
-- then KafkaBuildInterceptor.Reset():
  ProducerBuilder<string,string>.Build() -> Producer`2[Null,String]  isinst IProducer<String,String>=False  Produce -> EntryPointNotFoundException
  ProducerBuilder<string,byte[]>.Build() -> Producer`2[Null,String]  ... EntryPointNotFoundException
```
In other words, a suite that enables `<string,string>` tracking breaks any `ProducerBuilder<Null,string>`
in the app under test, the most common Confluent example type. ConsumerBuilder shares the defect (§5.2).

### 6.2 Interception stops after tier-up. VERIFIED (`tierup`)

`300 x new ProducerBuilder<string,string>(...).Build() after EnableProducerTracking<string,string>: tracked=151 firstUntrackedAt=151`.

### 6.3 Produce without a handler throws when delivery reports are disabled. VERIFIED (`dr-disabled`)

`TrackingKafkaProducer.Produce` always passes a non-null `wrappedHandler`
(`src/Kronikol.Extensions.Kafka/TrackingKafkaProducer.cs` L62-70, L81-89). Confluent throws when a
handler is given and `enable.delivery.reports=false` (`Producer.cs` L874-877):
```
built TrackingKafkaProducer`2 with EnableDeliveryReports=false
Produce (no handler) -> System.InvalidOperationException: A delivery handler was specified, but delivery reports are disabled.
same call on an unwrapped Producer`2 -> ok
```

### 6.4 Produce(handler) is attributed to the wrong test; a failed ProduceAsync logs nothing. VERIFIED (`attribution`)

The `CurrentTestInfoFetcher` reads an AsyncLocal. The producer was built under "Test A", `Produce(handler)`
was called under "Test B", and `ProduceAsync` under "Test C" (it failed with Local_MsgTimedOut):
```
logged: Request  test='Test A (built the producer)' id=A ... uri=kafka:///probe-topic activityTraceId=<null>
logged: Response test='Test A (built the producer)' id=A ... uri=kafka:///probe-topic activityTraceId=<null>
(2 entries in all)
```
Two causes:
- `LogProduce` runs inside the delivery handler on the owner's poll thread, under the owner's Build()
  context (§4.2). `KafkaTracker.LogOutgoing` resolves identity at log time
  (`src/Kronikol.Extensions.Kafka/KafkaTracker.cs` L293-296; resolution order in
  `src/Kronikol/Tracking/TestInfoResolver.cs` L19-31).
- `ProduceAsync` logs only after a successful await (`TrackingKafkaProducer.cs` L24-37), so a failed
  produce leaves no entry.

The same timing problem would apply to #135 part 2. Reading `Activity.Current` inside the delivery
handler would record the owner's Build()-time Activity (stopped), not the producer's span.
`RequestResponseLog` already has `ActivityTraceId`/`ActivitySpanId` (`src/Kronikol/Tracking/RequestResponseLog.cs`
L69-70), and `MessageTracker` fills them from `Activity.Current` at log time
(`src/Kronikol/Tracking/MessageTracker.cs` L144-145). VERIFIED (read).

---

## 7. Q4: Activity and traceparent; instrumentation packages

### 7.1 Confluent.Kafka itself: none. VERIFIED

`grep -rn 'ActivitySource|System.Diagnostics.Activity|new Activity(|Activity.Current|DiagnosticSource|DiagnosticListener|traceparent|tracestate|OpenTelemetry'`
over `src/` at **v2.15.1 and v2.16.0-RC1** finds nothing. A case-insensitive `activity` finds nothing
either. The csproj references only `librdkafka.redist` (plus System.Memory and System.Buffers for
netstandard2.0 and net462), with no DiagnosticSource (`Confluent.Kafka.csproj` L27-34). Kafka headers
are application data. Nothing is injected unless a wrapper does it.

### 7.2 OpenTelemetry.Instrumentation.ConfluentKafka (opentelemetry-dotnet-contrib). VERIFIED (source on `main` @ ee213d52, 2026-09-29)

- Status: prerelease only, latest **0.3.0-alpha.2** (released 2026-Sep-18, CHANGELOG.md L8-10),
  691,075 downloads (NuGet search API, 2026-10-07). It follows semantic conventions **v1.44.0**
  (`ConfluentKafkaCommon.cs` L19-21, L37).
- ActivitySource and Meter are named after the assembly, **"OpenTelemetry.Instrumentation.ConfluentKafka"**
  (`ConfluentKafkaCommon.cs` L39-40; `src/Shared/ActivitySourceFactory.cs` L36-40 uses
  `assemblyName.Name`). README L117-126 tells users to `.AddSource("OpenTelemetry.Instrumentation.ConfluentKafka")`
  when not using the DI helpers.
- **Entry point:** `public sealed class InstrumentedProducerBuilder<TKey,TValue> : ProducerBuilder<TKey,TValue>`.
  `Build()` returns `new InstrumentedProducer<TKey,TValue>(base.Build(), options, ((ClientConfig)this.Config).BootstrapServers)`
  (`InstrumentedProducerBuilder.cs` L13, L42-45). `InstrumentedConsumerBuilder` does the same with
  `base.Build()` (L13, L42-46).
  - Under Kronikol's Harmony patch the order is Instrumented (outer) → TrackingKafka (inner) → Producer.
  - `AsInstrumentedProducerBuilder()` copies a plain builder's internals through reflection on the
    `internal protected` properties (`OpenTelemetryProducerBuilderExtensions.cs` L49-61, L80-105).
  - **No DependentProducerBuilder support** (grep).
  - It fetches the cluster id with `new DependentAdminClientBuilder(handle).Build()` on the producer's
    Handle (`ConfluentKafkaCommon.cs` L116-123).
- **Producer span:**
  - Named `"send {topic}"` (`string.Concat(SendOperationName, " ", topic)`, `InstrumentedProducer.cs`
    L326), `ActivityKind.Producer`, `parentContext: default`, so the parent is the ambient
    Activity.Current (L342-348).
  - Before 0.2.0-alpha.1 it was `"{topic} publish"` (CHANGELOG.md L38-55, "Breaking change", 0.2.0-alpha.1
    released 2026-Jul-08: "The producer `send` span (previously `publish`) and the consumer `poll` span
    (previously `receive` ...)").
  - Started, and its context injected into `message.Headers`, **before** the inner call, for ProduceAsync
    (L46-51 then L57) and Produce (L141-151).
  - For the sync `Produce` the span is disposed when `Produce` returns, before the delivery report
    (`using var activity`, L142).
- **Header injection:** `Propagators.DefaultTextMapPropagator.Inject(new PropagationContext(activity.Context, Baggage.Current), message, ...)`
  appends to `message.Headers` (L379-386).
  - The OpenTelemetry API default propagator is **Noop** until the SDK's static constructor replaces
    it with `CompositeTextMapPropagator(TraceContextPropagator, BaggagePropagator)`. It also sets
    `Activity.DefaultIdFormat = W3C` (opentelemetry-dotnet `src/OpenTelemetry/Sdk.cs` L22-31;
    `src/OpenTelemetry.Api/Context/Propagation/Propagators.cs` L11-19).
  - The TraceContext header names are `"traceparent"` and `"tracestate"` (`TraceContextPropagator.cs`
    L20-21), plus `baggage`.
- **Consumer spans:**
  - **`"poll {topic}"`, `ActivityKind.Client`.** Created *after* the inner Consume returns, back-dated
    to its start (`InstrumentedConsumer.cs` L51-73, L295-321, L337-386). It **links** to the extracted
    producer context and does not take it as parent (L350-352, `parentContext: default` L383).
  - **`"process {topic}"`, `ActivityKind.Consumer`.** Only via `ConsumeAndProcessMessageAsync`, which
    **throws `ArgumentException("Invalid consumer type.")` unless the consumer is the
    `InstrumentedConsumer` itself** (`OpenTelemetryConsumeResultExtensions.cs` L74-127, check at
    L88-91). It also links to the producer context (L140-142, `parentContext: default` L171).
  - Consequences for Kronikol:
    - the consumer's spans live in the consumer's ambient trace (or a new root), not the producer's;
    - wrapping an `InstrumentedConsumer` *outside* with Kronikol's `.Tracked()` would break
      `ConsumeAndProcessMessageAsync`. The Harmony order (Kronikol inner) does not.
    - Inference from the code above; UNVERIFIED by running.

### 7.3 Confluent.Kafka.Extensions.Diagnostics, plus Confluent.Kafka.Extensions.OpenTelemetry (Vadim Hatsura). VERIFIED (source @ fb27a636 and 423a9af3)

- Downloads: Diagnostics **4,097,625** (latest 0.5.1-alpha.4; stable 0.5.0; nuspec depends on
  `Confluent.Kafka [2.3.0, 3.0.0)`). Extensions.OpenTelemetry **3,791,801** (0.4.0). Its only API is
  `AddConfluentKafkaInstrumentation()` = `builder.AddSource("Confluent.Kafka.Extensions.Diagnostics")`
  (`TracerProviderBuilderExtensions.cs`).
- ActivitySource **"Confluent.Kafka.Extensions.Diagnostics"**. Header names are the literal
  `"traceparent"` and `"tracestate"` (`ActivityDiagnosticsHelper.cs` L8-12).
- **Producer span `"{topic} publish"`**, ActivityKind.Producer, parent = ambient (L19-21). It writes
  `traceparent = activity.Id` and `tracestate` into `message.Headers` itself (L31-43). That Id is W3C
  only when `Activity.DefaultIdFormat` is W3C, the .NET 5+ default (UNVERIFIED).
- **Entry points** (`DependentProducerBuilderExtensions.cs` L11-18; `ProducerBuilderExtensions.cs`):
  - `BuildWithInstrumentation()` exists for **both ProducerBuilder and DependentProducerBuilder**. It
    calls `producerBuilder.Build()` (so a Harmony patch fires first) and wraps the result:
    InstrumentedProducer (outer) → Kronikol (inner) → Producer.
  - In its sync `Produce`, the span is started on the caller and stopped in the delivery handler on the
    poll thread (`InstrumentedProducer.cs` L57-73). So the caller's `Activity.Current` is still the
    publish span after `Produce` returns (UNVERIFIED by running; follows from §4.1).
- **Consumer span `"{topic} process"`**, ActivityKind.Consumer. **Its parent is set from the message's
  traceparent** (`SetParentId`), so the process span is a *child* of the producer span, in the same
  trace (`ActivityDiagnosticsHelper.cs` L79-123). It is started after `consumer.Consume()` returns,
  through `ConsumeWithInstrumentation(...)` (`ConsumerExtensions.cs` L11-29).

### 7.4 What this means for "which Activity's ids should an entry record". Analysis; inputs are VERIFIED above

- **Producer entry:**
  - For `ProduceAsync`, the instant after the inner await has the caller's context (§4.4). With an
    instrumentation wrapper outside Kronikol, `Activity.Current` there is the send/publish span.
  - For `Produce(handler)`, only the call instant on the caller's thread is right. The handler's
    context is the owner's Build()-time context (§4.2, §6.4).
  - Whatever the wrapper order, after the inner call returns, `message.Headers` holds the
    `traceparent` an instrumentation wrapper injected (both packages inject before calling inner).
    That gives the producer span's ids even when Kronikol's wrapper is the outer one. No package
    injects one when no instrumentation is present.
- **Consume entry:** Kronikol logs as the inner `Consume` returns (§4.5). At that moment neither
  package's poll or process span is current (OTel back-dates its poll span after the fact; both start
  process spans afterwards). The `traceparent` header on the consumed message is the only id available
  then: the producer span. OTel links to it and vhatsura parents to it. The consumer's processing span
  can only be captured later, for example by deferring or correlating, which is a design question.

---

## 8. Commands and environment (reproducible)

```bash
SP=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/77278bdc-858b-4173-96e8-50ec8ed0b557/scratchpad
# sources
curl -sSfL https://codeload.github.com/confluentinc/confluent-kafka-dotnet/tar.gz/refs/tags/v2.15.1 | tar -xz   # likewise v2.16.0-RC1, v2.0.2
gh api 'repos/confluentinc/confluent-kafka-dotnet/commits?path=src/Confluent.Kafka/DependentProducerBuilder.cs'
# probe 1 (pure Confluent + Harmony)
cd $SP/probe-dependent && dotnet build -c Release
dotnet bin/Release/net10.0/probe-dependent.dll <mode>     # and bin/Release/net8.0/...
#   modes: types threading flush harmony-dependent harmony-producer harmony-two-pairs harmony-consumer
#          harmony-unpatch-info harmony-dispatch-prefix harmony-tierup harmony-tierup-control harmony-canon harmony-handle
PROBE_STYLE=direct|virtual|delegate|producer|dispatch-direct [DOTNET_TieredCompilation=0|DOTNET_TieredPGO=0|DOTNET_JitNoInline=1] \
  dotnet bin/Release/net10.0/probe-dependent.dll harmony-tierup
# probe 2 (shipped Kronikol 4.9.0 packages)
cd $SP/probe-kronikol && dotnet build -c Release
dotnet bin/Release/net8.0/probe-kronikol.dll pairs|attribution|dr-disabled|tierup
```
Every mode exited 0. Full outputs:
- `probe-dependent/out-*-net10.txt`, `out-*-net8.txt`, `out-tierup-matrix-net10.txt`,
  `out-tierup-matrix-net8.txt`, `out-tierup-control-net10.txt`, `out-harmony-canon-net*.txt` and
  `out-harmony-handle-net*.txt`
- `probe-kronikol/out-*-net8.txt`

`out-harmony-tierup-net10.txt` is the first, noisy run (one line per build) and is superseded by the
matrix files.

Header line of every probe-1 run:
`runtime=.NET 10.0.11 (or .NET 8.0.27) Confluent.Kafka=2.15.1+12fb6718875ef13251fd5bad65b24c167407d519 librdkafka=2.15.1 Harmony=2.4.2.0`.

---

## 9. Source index (all fetched 2026-10-07)

- Confluent.Kafka trees: `scratchpad/ck-src/full-v2.15.1`, `full-v2.16.0-RC1`, `full-v2.0.2`.
  Single files are in `ck-src/v2.15.1/`, and `ck-src/dpb-v1.0.0.cs` is v1.0.0's DependentProducerBuilder.
- librdkafka: `scratchpad/rdkafka-v2.15.1.c` (from `confluentinc/librdkafka` tag v2.15.1).
- dotnet/runtime release/8.0: `scratchpad/dotnet-runtime/` (Task.cs, ThreadPoolTaskScheduler.cs,
  Activity.cs, Activity.Current.net46.cs, clrconfigvalues.h).
- OpenTelemetry contrib ConfluentKafka (main): `scratchpad/otel-kafka/`. It also holds Sdk.cs,
  Propagators.cs and ActivitySourceFactory.cs.
- vhatsura packages: `scratchpad/vhatsura/`.
- Harmony docs: `scratchpad/harmony-docs/` (patching-edgecases.md, patching-injections.md).
  Issues: pardeike/Harmony #201, #307, #391, #407, #504, #526; MonoMod/MonoMod #187.
- Kronikol (worktree `C:/Code/Kronikol-kafka135` @ 417c8e58, read only):
  - `src/Kronikol.Extensions.Kafka.BuildInterception/KafkaBuildInterceptor.cs`
  - `src/Kronikol.Extensions.Kafka/{KafkaTrackingInterceptor,TrackingKafkaProducer,TrackingKafkaConsumer,KafkaTracker,KafkaTrackingOptions}.cs`
  - `src/Kronikol/Tracking/{TestInfoResolver,TestIdentityScope,RequestResponseLog,MessageTracker}.cs`
  - `tests/Kronikol.Tests.Kafka.BuildInterception/KafkaBuildInterceptorTests.cs`
  - issue #135 (`gh issue view 135 --repo lemonlion/Kronikol`)
