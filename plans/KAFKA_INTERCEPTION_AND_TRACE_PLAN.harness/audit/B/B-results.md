# Audit B: KAFKA_INTERCEPTION_AND_TRACE_PLAN re-run on 4.14.6 and experiments on the proposed design

Run 2026-10-12 on Windows 11, .NET 8.0.27 and 10.0.11 (SDK 10.0.300). Plan and harness read from
`C:/Code/Kronikol-audit135/plans/` (worktree at `d59843fa`, 4.14.6; plan files untracked there, not touched).
Every probe is a console app (or, for E5, a test project) under this directory; each mode runs in its own
process because a Harmony patch on shared generic code cannot be undone. Labels: **RUN** (measured here; command
and output file named), **READ** (source), **INFERRED**.

## 0. Versions and what could not be held

| Package | Declared by | Floor | A fresh consumer of 4.14.6 resolves | Used here |
|---|---|---|---|---|
| Kronikol.Extensions.Kafka(.BuildInterception) | | | 4.14.6 | 4.14.6 (published 2026-10-10) |
| Confluent.Kafka | Kronikol.Extensions.Kafka 4.14.6 nuspec | `>= 2.16.0` | 2.16.0 | 2.16.0 |
| Lib.Harmony | Kronikol.Extensions.Kafka.BuildInterception 4.14.6 nuspec | `>= 2.4.2` | 2.4.2 | 2.4.2, and 2.4.3 where marked `h243` |

- **Confluent.Kafka 2.15.1 cannot be held with 4.14.6 (RUN).** `dotnet restore -p:ConfluentVersion=2.15.1` in
  `s0-shipped`: `error NU1605: Warning As Error: Detected package downgrade: Confluent.Kafka from 2.16.0 to 2.15.1 ...
  Kronikol.Extensions.Kafka 4.14.6 -> Confluent.Kafka (>= 2.16.0)`. Forced with `-p:NoWarn=NU1605`, the first call
  fails: `System.IO.FileNotFoundException: Could not load file or assembly 'Confluent.Kafka, Version=2.16.0.0'`
  (Confluent's assembly version moves with every release: 2.15.1.0, 2.16.0.0). Every 4.14.6 result below is on
  Confluent.Kafka 2.16.0. The floor moved at **4.10.0** (published 2026-10-08): 4.9.0 and 4.9.1 declare 2.15.1, 4.10.0
  on declare 2.16.0, because `src/Kronikol.Extensions.Kafka/Kronikol.Extensions.Kafka.csproj` floats `Confluent.Kafka 2.*`
  and 2.16.0 shipped on 2026-10-07. A suite pinned to 2.15.1 (the #135 reporter's) cannot take Kronikol 4.10.0 or later
  without moving Confluent.
- **Lib.Harmony 2.4.3** was published 2026-10-11, after 4.14.6 was packed, so 4.14.6 declares 2.4.2. The repo's own
  `Lib.Harmony 2.*` floats: its next pack will declare `>= 2.4.3`, and its tests already restore 2.4.3. Only a project
  that pins or floats gets 2.4.3 today; a fresh consumer of the 4.14.6 package gets 2.4.2 (s0-kronikol's
  `project.assets.json`: `Lib.Harmony/2.4.2`, `Confluent.Kafka/2.16.0`).

## 1. Part 1: the plan's probes on 4.14.6

Retargeted copies: `s0-shipped/`, `s0-kronikol/` (net10.0 added; `attribution` prints the delivery report's error and
each entry's status and error, otherwise unchanged), `prototype/`. Recorded 4.9.0 outputs copied to `recorded/`.

| # | Claim | Recorded at 4.9.0 (Confluent 2.15.1, Harmony 2.4.2) | 4.14.6, Confluent 2.16.0, Harmony 2.4.2 (net8.0 and net10.0) | 4.14.6, Harmony 2.4.3 | Verdict |
|---|---|---|---|---|---|
| C1 | `DependentProducerBuilder` not intercepted | `Producer`2<String,String>` | same, both runtimes (`s0-shipped/results-4.14.6.txt`) | not re-run | **Reproduces** |
| C2 | no W3C ids | `ActivityTraceId: [null, null]` | same, both runtimes | not re-run | **Reproduces** |
| C6 | content is text | `Key: k, Value: {"a":1}` | same | not re-run | **Reproduces** |
| F1 | other reference-type pairs get the wrong client | `out-pairs-net8.txt` | byte-identical after the header, both runtimes (`s0-kronikol/out-pairs-*.txt`) | byte-identical (`s0-kronikol-h243/out-pairs-*.txt`) | **Reproduces**, and wider than stated (E2) |
| F2 | tier-up ends interception | first bypass 151 (net8) | net8.0: **47, 167, 201**; net10.0: 101, 151, 251 (`s0-kronikol/out-tierup.txt`) | net8.0: 163, 128, 117; net10.0: 131, 251, 101 (`s0-kronikol-h243/out-tierup.txt`) | **Reproduces**; 47 is below the plan's "116 to 151" |
| F2 remedy | quick JIT off holds | 20,000/20,000 (generic probe) | `DOTNET_TC_QuickJit=0` tierup: 300/300 both runtimes | 300/300 both runtimes (`out-tierup-quickjit0.txt`) | **Holds** on both Harmony versions |
| F4 | sync `Produce` is the builder's test | entries A, A | A, A, both runtimes | not re-run | **Reproduces** |
| F5 | failed delivery drawn as success | timed out, logged as produce | delivery report `Error.IsError=True Code=Local_MsgTimedOut Status=NotPersisted`; both entries `status=<null> error=<null>` | not re-run | **Reproduces** |
| F6 | failed `ProduceAsync` logs nothing | `(2 entries in all)` | `ProduceAsync threw ProduceException`2 (Local_MsgTimedOut)`, `(2 entries in all)`, none for test C | not re-run | **Reproduces** |
| F7 | delivery reports off: plain `Produce` throws | `InvalidOperationException` | identical, both runtimes | not re-run | **Reproduces** |
| proto | dispatching prefix gives correct types | 5 of 5 `ok=True` | identical, both runtimes (`prototype/results-4.14.6.txt`, `exp/out/proto-h242-*.txt`) | identical (`exp-h243/out/proto-h243-*.txt`) | **Holds** |

Commands: `dotnet build -c Release` then `dotnet bin/Release/<tfm>/probe-kronikol.dll <mode>` (pairs, dr-disabled,
attribution once per runtime; tierup three times per runtime); `dotnet bin/Release/<tfm>/Probe.dll` for s0-shipped and
prototype.

### Harmony 2.4.3 re-run (coordinator's request)

- `s0-kronikol-h243/` (Lib.Harmony 2.4.3 pinned, prints `Lib.Harmony loaded: 2.4.3.0`): `pairs` identical to 2.4.2 on
  both runtimes; `tierup` as in the table.
- `tiermit/` harness program verbatim in four builds (`h242-qjon`, `h242-qjoff`, `h243-qjon`, `h243-qjoff`), results in
  `tiermit/results.txt`:

| Build | net8.0 M0 | net10.0 M0 | quick JIT off M5 (20,000 calls after warm-up) |
|---|---|---|---|
| Harmony 2.4.2, quick JIT on | bypass at 251, 251 | 251, 251 | n/a |
| Harmony 2.4.2, `<TieredCompilationQuickJit>false` | 2000/2000 twice | 2000/2000 twice | 20000/20000 both runtimes |
| Harmony 2.4.3, quick JIT on | bypass at 251, 251 | 251, 301 | n/a |
| Harmony 2.4.3, `<TieredCompilationQuickJit>false` | 2000/2000 twice | 2000/2000 twice | 20000/20000 both runtimes |
| either, `DOTNET_TC_QuickJit=0` env | 2000/2000 | 2000/2000 | |

- Prototype and the E2/E3 partial-sharing and stacking checks on 2.4.3 (`exp-h243/out/`): `proto`, `e2a-int`,
  `e2a-str`, `e2b`, `e2c`, `e3-dup`, `e3-stack` outputs identical to 2.4.2 on both runtimes.
- **Answer:** F1 and F2 reproduce on Harmony 2.4.3 exactly as on 2.4.2, and quick JIT off still holds there.

## 2. Experiments on the proposed design

The experiment program is `exp/Program.cs` (Exp.csproj: BuildInterception 4.14.6, Confluent.Kafka 2.16.0, Lib.Harmony
2.4.2; `exp-h243/` is the same source on 2.4.3). `Proto.Prefix` is the harness prototype's prefix verbatim (strategy 0)
plus two variants for E1 (strategy 1: `BindingFlags.DoNotWrapExceptions`; strategy 2: a cached compiled
`Expression.New` + `WrapProducer` delegate). Command: `dotnet bin/Release/<tfm>/win-x64/Exp.dll <mode> [variant]`.

### E1. Exception wrapping (RUN, `exp/out/e1-*.txt`, identical on net8.0 and net10.0)

| Config Confluent rejects | (a) unpatched `Build()` | (b) prototype prefix | (c) `DoNotWrapExceptions` | (d) compiled delegate |
|---|---|---|---|---|
| `no.such.property=x` | `InvalidOperationException: No such configuration property: "no.such.property"` | `TargetInvocationException [inner InvalidOperationException]` | as (a) | as (a) |
| `acks=banana` | `ArgumentException: Invalid value for configuration property "request.required.acks"` | `TargetInvocationException [inner ArgumentException]` | as (a) | as (a) |
| `delivery.report.only.error=true` | `ArgumentException` | `TargetInvocationException [inner ArgumentException]` | as (a) | as (a) |
| `dotnet.producer.enable.delivery.reports=maybe` | `FormatException` | `TargetInvocationException [inner FormatException]` | as (a) | as (a) |
| `ProducerBuilder<Uri,string>`, no key serializer | `ArgumentNullException` | `TargetInvocationException [inner ArgumentNullException]` | as (a) | as (a) |

Meaning: as written, the prototype changes the exception type of every failing `Build()` for a patched shape, tracked
pair or not; a caller catching `ArgumentException`/`KafkaException`-style errors at startup stops catching them. Both
fixes surface the same type and message as Confluent. The compiled delegate is also what keeps the tier-up window from
shrinking (section 3, X2). Add a fact: a config Confluent rejects surfaces the same exception type through the patched
`Build()` as through Confluent's.

### E2. Partial generic sharing (RUN)

(a) **Shipped 4.14.6** (`exp/out/e2a-*.txt`, identical net8.0/net10.0 and Harmony 2.4.2/2.4.3):

```
EnableProducerTracking<int,string>() and EnableConsumerTracking<int,string>() only:
  ProducerBuilder<Int32,String>.Build() -> TrackingKafkaProducer<Int32,String>   Produce -> ok
  ProducerBuilder<Int32,Byte[]>.Build() -> TrackingKafkaProducer<Int32,String>   is IProducer<Int32,Byte[]>=False  Produce -> EntryPointNotFoundException
  ProducerBuilder<Int32,Null>.Build()   -> TrackingKafkaProducer<Int32,String>   is IProducer<Int32,Null>=False    Produce -> EntryPointNotFoundException
  ProducerBuilder<Int64,String>.Build() -> Producer<Int64,String>                Produce -> ok
  ConsumerBuilder<Int32,Byte[]>.Build() -> TrackingKafkaConsumer<Int32,String>   Subscribe -> EntryPointNotFoundException
EnableProducerTracking<string,int>() only:
  ProducerBuilder<Null,Int32>.Build()   -> TrackingKafkaProducer<String,Int32>   Produce -> EntryPointNotFoundException
  ProducerBuilder<Byte[],Int32>.Build() -> TrackingKafkaProducer<String,Int32>   Produce -> EntryPointNotFoundException
  ProducerBuilder<String,Int64>.Build() -> Producer<String,Int64>                Produce -> ok
```

(b) **Prototype prefix patched on `ProducerBuilder<int,string>.Build` only**, tracking enabled for `<int,string>`,
`<int,byte[]>`, `<long,string>` (`exp/out/e2b-*.txt`):

```
  <Int32,String> -> TrackingKafkaProducer<Int32,String>  prototypePrefixRan=1
  <Int32,Byte[]> -> TrackingKafkaProducer<Int32,Byte[]>  prototypePrefixRan=1   (shared code: right type, tracked)
  <Int64,String> -> Producer<Int64,String>  tracked=False prototypePrefixRan=0   (enabled, but its shape is not patched)
  <KeyValuePair<Uri,Uri>,String> -> TrackingKafkaProducer<...>  prefixRan=1    (patch was on <KeyValuePair<string,string>,string>)
  <KeyValuePair<String,Int32>,String> -> Producer<...>  prefixRan=0
  SubBuilder : ProducerBuilder<string,string>, Build() -> ArgumentException: The number of generic arguments provided
    doesn't equal the arity of the generic type definition.
```

(c) **Patching through the shape's representative** (`exp/out/e2c-*.txt`): with `Canon(t)` = `object` for every
reference type, recursively inside generic value types, patching `ProducerBuilder<Canon(K),Canon(V)>.Build` gives one
`PatchInfo` per shape (`<object,object>`: 1 prefix; `<int,object>`: 1 prefix), a second pair of the same shape finds it
patched, and `<string,string>`, `<Null,string>`, `<int,byte[]>`, `<int,string>`, `<KeyValuePair<Uri,Uri>,string>` all
return their own correctly typed client, tracked exactly when enabled.

Meaning: F1's statement and T6's guard are wrong as written ("value-type pairs have their own code and are
untouched"): a value-type pair shares its code with every pair that has the same value types in the same positions
(`<int,string>` with `<int,byte[]>`, `<int,Null>`; `<string,int>` with `<Null,int>`), and the shipped interception
corrupts those too. Patches must be keyed per canonical shape (each argument: the exact value type, or "reference";
recursively for generic value types such as `KeyValuePair<,>`), and every `Enable…<K,V>` must ensure its own shape is
patched (an enabled `<long,string>` is otherwise silently untracked). T6 should build `<int,byte[]>`/`<int,Null>` with
`<int,string>` enabled (red on 4.14.6). The prototype's `__instance.GetType().GetGenericArguments()` throws for a builder
subclass even when its pair is not enabled; the plan's "walks base types" is required, and T7 should include a
non-generic subclass.

### E3. Concurrency and stacking (RUN)

Stacking (`exp/out/e3-dup-*.txt`, `e3-stack-*.txt`; same on Harmony 2.4.3):

```
  a void pass-through prefix patched once: ran 1x per Build()
  the same prefix patched a second time: ran 2x per Build(); GetPatchInfo: 2 prefix(es)
  plus the dispatching prefix patched twice: pass-through ran 2x, dispatching prefix ran 1x; handles left 0
```

`Harmony.Patch` is not idempotent: every call appends a prefix entry. The dispatching prefix runs once only because
Harmony skips a later result-affecting (bool, `ref __result`) prefix once one has returned false; a second producer is
never built (`Library.HandleCount` unchanged). Patching another closed `MethodInfo` of the same shared code
(`<Null,string>.Build` after `<string,string>.Build`) or from a second Harmony id did not change the outcome.

Ten threads run enable+patch together behind a barrier, each then builds its producer; four threads build
`<Null,string>`/`<string,string>` throughout (`exp/out/e3-race-*.txt`):

| Guard | net8.0 | net10.0 | PatchInfo prefixes after | Background anomalies |
|---|---|---|---|---|
| none (the prototype's literal path) | 10/10 first builds tracked | 10/10 | 10 | 0 |
| check, patch, then mark | 10/10 | 10/10 | 10 | 0 |
| `TryAdd`, then patch (the shipped guard) | **9 of 10 untracked** | **9 of 10 untracked** | 1 | 0 |
| **shipped `KafkaBuildInterceptor` 4.14.6**, 2 runs per runtime | **9 of 10 untracked** each run | **9 of 10** each run | (postfix) | 2 to 4 `<Null,string> -> TrackingKafkaProducer<String,String>` (F1) |
| `lock` around check and patch | 10/10 | 10/10 | 1 | 0 |

In every variant all ten hosts' producers carried one host's options (`tracked, options of host7` for all ten, etc.).

Meaning: no exception, wrong type or leaked handle under concurrency with the dispatching prefix. But the shipped guard
loses the first build of every host that loses the `TryAdd` race (they return before the winner's patch exists): a host
that builds its producer once at startup stays untracked for the run. That is a live 4.14.6 defect for the reporter's
ten in-process hosts, and the plan's "patch once per shape" must use a lock (or `Lazy`) and only return once the patch
is in place. Separately, the per-pair tracking dictionary keeps the last host's options for every host (E8c too).

### E4. Disable path (RUN, `exp/out/e4-*.txt`, identical net8.0/net10.0)

With the prototype patch installed and `DisableProducerTracking<string,string>()` called: `<string,string>`,
`<Null,string>` and `<string,byte[]>` are each Confluent's own `Producer<K,V>`, correctly typed; `Produce` without a
handler works and `Produce` with a handler gets its delivery report (`Local_MsgTimedOut (NotPersisted)`, no broker).
Re-enabling tracks again; `KafkaTrackingInterceptor.Reset()` untracks. Meaning: the never-unpatch design is sound on this
path.

### E5. Tiering detection (RUN)

Probe `e5/tierdetect-{default,qjoff,tcoff}/` (net8.0 and net10.0, Harmony 2.4.2) prints what the plan's check would read
and measures a shared-generic Harmony prefix over 2,000 calls. Full output `e5/tierdetect-results.txt`,
`e5/tierdetect-parsing.txt`.

| Case | `AppContext...TieredCompilation` / `...QuickJit` | Env | Runtime obeyed (measured, net8 / net10) |
|---|---|---|---|
| (a) default | `null` / `null` | none | re-JIT: bypass 501 / 251 |
| (b) csproj `TieredCompilationQuickJit=false` | `null` / `String "false"` | none | no re-JIT: 2000/2000 |
| (c) default | `null` / `null` | `DOTNET_TC_QuickJit=0` | no re-JIT |
| (d) default | `null` / `null` | `DOTNET_TieredCompilation=0` | no re-JIT |
| **(e) csproj false** | `null` / `String "false"` | `DOTNET_TC_QuickJit=1` | **re-JIT: bypass 701 / 201 (env wins)** |
| (f) default | `null` / `null` | `DOTNET_TC_QuickJit=false` | **re-JIT (quick JIT ON)** |
| (g) default | `null` / `null` | `COMPlus_TC_QuickJit=0` | no re-JIT |
| (h) csproj `TieredCompilation=false` | `String "false"` / `null` | none | no re-JIT |
| (i) csproj `TieredCompilation=false` | `String "false"` / `null` | `DOTNET_TieredCompilation=1` | re-JIT: bypass 251 (env wins) |
| **(j) csproj QuickJit false** | `null` / `String "false"` | `DOTNET_TC_QuickJit=false` | **re-JIT: bypass 801 / 701** |
| extra | | `DOTNET_TC_QuickJit=true` or `zzz` | ignored: default build re-JITs, qjoff build holds |
| extra | | `=0x0`, `=00` | off |
| extra | | `DOTNET_TieredCompilation=false` | tiering stays ON |
| extra | | `DOTNET_TC_QuickJit=1` + `COMPlus_TC_QuickJit=0` | ON (`DOTNET_` beats `COMPlus_`) |

`AppContext.GetData` returns a `String` ("false") when the csproj sets the property, `null` otherwise, and never reflects
the environment. In (e) the runtime obeyed the environment variable, not the csproj. A value is read as a hexadecimal
number: `false` starts with the hex digits `fa`, so `DOTNET_TC_QuickJit=false` turns quick JIT **on** (and overrides the
csproj), while `true`/`zzz` have no hex prefix and are ignored (INFERRED from the measurements: `fa` = 0xFA, nonzero).
A reader that consults runtimeconfig first, or treats "false" as off, gives the wrong verdict in (e), (f), (i), (j); one
that reads the environment first but treats non-numbers as unset still misses (j). The check must read `DOTNET_X`, then
`COMPlus_X`, parse the leading hex digits (optional `0x`), treat no digits as unset, and only then fall back to the
runtimeconfig string (`"false"`).

`buildTransitive` props (Q1): a local package `Probe.QuickJitOff` 1.0.0 (`e5/pkg/`, feed `e5/feed/`, `e5/nuget.config`)
with only `buildTransitive/Probe.QuickJitOff.props` setting `TieredCompilationQuickJit` to false unless already set.

| Consumer | runtimeconfig.json (net8.0 and net10.0) | Measured in the test process (`e5/props-results.txt`) |
|---|---|---|
| xunit.v3 3.2.2 test project, direct `PackageReference` | `"System.Runtime.TieredCompilation.QuickJit": false` | `Consumer.exe`, AppContext `false`, 2000/2000 |
| xunit.v3 test project, `ProjectReference` to a library that references the package | `false` | 2000/2000 |
| xunit.v3 test project that sets `<TieredCompilationQuickJit>true</TieredCompilationQuickJit>` | `true` | bypass at 401 (net8) / 551 (net10) |
| xunit 2.9.3 under VSTest (runs in `testhost.exe`) | `false` | `testhost.exe`, 2000/2000 |

Meaning: Q1's props works, flows transitively, is overridable by the project, and reaches both the xunit.v3 test
executable and VSTest's testhost. It does not survive an environment variable (case e), so the diagnostic is still
needed even with the props. INFERRED (NuGet semantics, not run): `PrivateAssets="all"` or `ExcludeAssets="buildTransitive"`
on the reference stops the flow.

### E6. Confluent's F7 path (RUN + READ)

RUN (`exp/out/e6-*.txt`, identical net8.0/net10.0, Confluent 2.16.0):

```
-- (i) Producer<string,string>, dotnet.producer.enable.delivery.reports=false
  out-queue length before: 0
  Produce(topic, msg, handler) -> InvalidOperationException: A delivery handler was specified, but delivery reports are disabled.
  out-queue length after the refused call: 0
  out-queue length after a plain Produce: 1
  ProduceAsync with reports off -> returns at once: offset=Unset [-1001] status=NotPersisted
-- (ii) ProducerBuilder<String,String>: Config found=True (IsFamilyOrAssembly=True) dotnet.producer.enable.delivery.reports='False'
  SubBuilder: Config found=True ... 'False'; a builder that never set it: '<absent>'
  built Producer<String,String>: private field enableDeliveryReports=False
-- (iii) DEPENDENT producer on an owner with delivery reports off, wrapped by the 4.14.6 TrackingKafkaProducer
  dependent producer's own enableDeliveryReports field=True
  (owner reports off).Produce -> ok;  (owner reports on).Produce -> ok;  tracked owner (reports off).Produce -> InvalidOperationException (F7)
  entries logged for dep-of-owner-reports-off: 0
  entries logged for dep-of-owner-reports-on: 2
-- (iv) ObjectDisposedException is InvalidOperationException: True
```

READ (Confluent 2.15.1 `Producer.cs`; 2.16.0-RC1 `Impl/SafeKafkaHandle.cs`, `Impl/SafeConfigHandle.cs`):
- `:874-877`: the refusal is the first statement of `Produce(TopicPartition, …)`, before serialization and
  `ProduceImpl`; (i) confirms nothing is queued.
- Every other `InvalidOperationException` in Confluent's produce paths: `:886` and `:905` ("Produce called with an
  IAsyncSerializer … ISerializer is required") are thrown inside `try { } catch (Exception ex)` and rethrown as
  `ProduceException<K,V>` (`Local_KeySerialization`/`Local_ValueSerialization`), so they never escape as IOE;
  `:529`/`:552` (both sync and async serializers) and `SafeConfigHandle.Set` (unknown property) and
  `SafeKafkaHandle.Create` (`kafka_new` failure) throw at construction, i.e. at `Build()`, not at produce.
  `ProduceAsync` has no IOE of its own (serializer failures and `KafkaException` become `ProduceException`; a cancelled
  token gives `TaskCanceledException`). `SafeKafkaHandle.Produce` (RC1 `:362-440`) does not call `ThrowIfHandleClosed`, so
  Confluent's produce does not throw `ObjectDisposedException`, though other members (`Flush`, `Poll`…) do.
- **Double send:** not with Confluent's own producer (the refusal precedes queueing). It can with any other inner:
  `TrackingKafkaProducer` wraps whatever `IProducer` it is given (DI decoration, `.Tracked()`), and `IProducer`'s
  contract allows IOE "for application logic errors" at any point; a retry repeats the decorator's side effects.
  **Masking:** a bare `catch (InvalidOperationException)` also catches `ObjectDisposedException` (iv) and any decorator's
  own IOE, and would wrongly mark the producer "reports off".
- **Alternative (RUN):** the setting is readable inside the prefix: `ProducerBuilder<,>.Config` is `protected internal`
  and reflection finds it on the instance (also on a subclass); the built `Producer<,>`'s private `enableDeliveryReports`
  is readable too. That decides once, without exceptions, for intercepted builds; the catch stays only for producers
  wrapped by `.Tracked()`/DI, narrowed to `ex.GetType() == typeof(InvalidOperationException)`, the caller passing no
  handler, and Confluent's own producer as the inner.
- **New silent loss (RUN):** a dependent producer on an owner with delivery reports off throws nothing (its own field
  defaults to true) but its handler never runs (the owner's librdkafka config registered no `dr_msg_cb`), so a tracked
  dependent producer logs nothing at all; READ: each such produce allocates a `GCHandle` that only the delivery callback
  frees. Neither the retry nor the builder's `Config` (a dependent builder has none; `Handle.Config` exists only from
  2.16.0) catches it; reading the owner's `enableDeliveryReports` through `Handle.Owner` (internal, present 2.14.0 to
  2.16.0) does. R1's dependent-builder interception needs a fact for it.

### E7. Latest Confluent (RUN, `exp/out/e7-*.txt`)

nuget.org lists Confluent.Kafka up to **2.16.0** (published 2026-10-07, after the plan's 2.16.0-RC1 reading); no 3.x.
Reflection on the package DLLs (each in its own collectible `AssemblyLoadContext`):

| | 2.14.0 net8.0 | 2.15.1 net8.0/net10.0 | 2.16.0 net8.0/net10.0 |
|---|---|---|---|
| `Producer(DependentProducerBuilder<K,V>)`, `Producer(ProducerBuilder<K,V>)`, `Consumer(ConsumerBuilder<K,V>)` | internal, present | present | **present** |
| `ProducerBuilder`/`DependentProducerBuilder`/`ConsumerBuilder` `.Build()` IL | `02 73 <ctor> 2A` (7 bytes: ldarg.0, newobj, ret), virtual | same | **same** |
| `ProducerBuilder.Config` | protected internal | same | same |
| `Producer.enableDeliveryReports`, `Handle.Owner` | present | present | present |
| `Handle.Config` | absent | absent | **present (new)** |
| `DependentProducerBuilder` public properties | Handle + 4 serializers | same | **+ KeySerializerBuilder, ValueSerializerBuilder, AsyncKeySerializerBuilder, AsyncValueSerializerBuilder (EXPERIMENTAL)** |

Meaning: the internal constructors and the 7-byte `Build()` bodies hold in 2.16.0, so a prefix that replaces `Build()`
loses nothing; the 2.16 serializer builders are resolved inside the internal constructor (RC1 `Producer.cs:661-690`,
READ), so they are kept too. The plan's "`DependentProducerBuilder` is unchanged since 1.0.0" is no longer true of its
public surface. Every 4.14.6 run here already exercised 2.16.0 end to end.

### E8. Duplicate tracker registration (RUN, `exp/out/e8*-net8.0.txt`)

| Case | Registered | Unused after the suite's traffic | Names in the warning |
|---|---|---|---|
| (a) `KafkaTrackingInterceptor.EnableProducerTracking<string,string>` twice, one wrapped producer, one produce (real producer, no broker; the wrapped handler logs on the timed-out delivery) | 2 | **1** | `KafkaTracker (Kafka)` |
| (b) `KafkaBuildInterceptor.EnableTracking<string,string>` (both directions), producer-only app, one produce | 2 | **1** (the consumer tracker) | `KafkaTracker (Kafka)` |
| (c) ten in-process hosts each `KafkaBuildInterceptor.EnableProducerTracking<string,string>` with its own `CallerName`, each builds and produces once | 10 | **9** | `KafkaTracker (Kafka)` x 9 |

In (c) every host's produce entry carries `CallerName host9`. Meaning: `ReportDiagnostics` would print "N tracking
component(s) were registered but never invoked … added to the wrong pipeline or options" for trackers the suite cannot
have meant to leave unused (replaced by a later `Enable…`, unreachable), and all carry the same name, so a reader cannot
tell the replaced producer tracker from a genuinely idle consumer tracker. Re-enabling a pair should reuse or unregister
the old tracker, and the name should carry the direction and pair.

## 3. Other findings

- **X1. Confluent floor (RUN).** Section 0: 4.10.0 and later require Confluent.Kafka >= 2.16.0 through a floating
  `2.*`; the same mechanism will raise the Harmony floor to 2.4.3 at the next pack.
- **X2. The tier-up window is about 30 builds, not ~100 (RUN, `exp/out/tierup-variants.txt`, `tierup-proto.txt`).**
  300 builds of `<string,string>`, first untracked build:

| Interception | Busy loop (50 ms pause per 50 builds) | With a 200 ms pause every 10 builds, as between tests |
|---|---|---|
| prototype prefix verbatim (reflection per build) | 33 to 40 in all 16 runs (net8/net10, Harmony 2.4.2/2.4.3, two call sites) | 34, 34, 34, 34 |
| prefix with compiled delegate | 60 to 201 | 40, 40, 40, 51 |
| shipped 4.14.6 postfix | 54 to 251 | 40, 40, 40, 40 |
| any of them with `DOTNET_TC_QuickJit=0` | 300/300 | |

  The larger numbers come from a short probe that keeps JITting new code; once a process has quiet periods, interception
  ends at about build 40 for every design, and at 33 to 40 for the prototype as written. The plan's diagnostic text
  ("can stop after the first ~100 builds") and its "116 to 151" understate the exposure; builds of every pair of a shape
  count together. INFERRED: 30 is the runtime's default call-count threshold, after which tier-1 code replaces the
  detoured code once the 100 ms call-counting delay passes without new JIT work.
- **X3. TryAdd race in the shipped guard (RUN, E3).** 9 of 10 concurrently enabling hosts' first builds untracked on
  4.14.6.
- **X4. Last `Enable…` wins for every host (RUN, E3, E8c).**
- **X5. Dependent producer on a reports-off owner logs nothing (RUN, E6 iii).**

## 4. What it means for the plan

1. Part 1: every finding checked (C1, C2, C6, F1, F2, F4 to F7) still reproduces on 4.14.6 with Confluent 2.16.0, on
   both runtimes and both Harmony versions where re-run; the prototype still returns correct types.
2. R0's prefix must not use bare `Activator.CreateInstance`/`MethodInfo.Invoke` (E1); use a cached compiled delegate per
   shape (also restores the tier-up window to the shipped one's, X2) or `DoNotWrapExceptions`.
3. Key patches by canonical shape, recursively, and patch through the representative instantiation (E2c); fix F1's text
   and T6; walk base types for subclasses (E2b).
4. Serialize "ensure patched" with a lock and return only after `Harmony.Patch` completes (E3); the shipped 4.14.6 guard
   has the race today (X3).
5. The tiering check must give environment variables precedence and parse them as hex (E5); the props of Q1 works and is
   overridable, but an environment variable defeats it, so keep the diagnostic.
6. F7: read the delivery-report setting at build time instead of catch-and-retry, narrow any remaining catch, and cover
   the dependent-producer silent loss (E6).
7. E4 and E7 raise nothing against the design; E8 adds a diagnostic-noise defect for multi-host suites.

## 5. Cleanup

All `bin/` and `obj/` directories created under this directory were deleted after the runs, and `e5/packages/` (the
local package folder holding only the probe package). Kept: every source, csproj, output file, the probe `.nupkg` in
`e5/feed/`. Restores added `kronikol.extensions.kafka(.buildinterception)/4.14.6` to the user's global NuGet cache
(published packages; left in place). Nothing in `C:/Code/Kronikol` or any worktree was touched.
