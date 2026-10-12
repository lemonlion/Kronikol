"""R0's wiki edits (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 6.4), applied after the release is published.

Usage: python r0_wiki.py <wiki-worktree> <version> [--check]
Every `old` must occur exactly once; --check reports without writing. The pages are LF.
"""
import sys

WIKI, VERSION = sys.argv[1], sys.argv[2]
CHECK = '--check' in sys.argv
V = VERSION

EDITS = {
    'Integration-Kafka-Extension.md': [
        ("| `Produce` | `IProducer.Produce` (with delivery handler) | Tracked |",
         f"| `Produce` | `IProducer.Produce`, with or without a delivery handler: logged when the delivery report arrives, or at the call, without a partition or offset, when the producer's delivery reports are off ({V}+) | Tracked |"),
        ("""services.AddSingleton(sp =>
{
    var options = new KafkaTrackingOptions
    {
        ServiceName = "Kafka",
        CallerName = "My API",
        CurrentTestInfoFetcher = () => /* ... */
    };
    return new KafkaTracker(options, sp.GetService<IHttpContextAccessor>());
});

services.AddSingleton<IProducer<string, string>>(sp =>
{
    var tracker = sp.GetRequiredService<KafkaTracker>();
    var inner = new ProducerBuilder<string, string>(config).Build();
    return new TrackingKafkaProducer<string, string>(inner, tracker, new KafkaTrackingOptions());
});
```""",
         """var kafkaOptions = new KafkaTrackingOptions
{
    ServiceName = "Kafka",
    CallerName = "My API",
    CurrentTestInfoFetcher = () => /* ... */
};

services.AddSingleton(sp => new KafkaTracker(kafkaOptions, sp.GetService<IHttpContextAccessor>()));

services.AddSingleton<IProducer<string, string>>(sp =>
{
    var tracker = sp.GetRequiredService<KafkaTracker>();
    var inner = new ProducerBuilder<string, string>(config).Build();
    return new TrackingKafkaProducer<string, string>(inner, tracker, kafkaOptions);
});
```

Give the wrapper the same options as the tracker: the wrapper reads its own for the message content, the identity headers and correlation, so a fresh `KafkaTrackingOptions` there ignores what the tracker was configured with."""),
        ("| `CurrentStepTypeFetcher` | `Func<string?>?` | `null` | Optional — returns the current BDD step type (Given/When/Then) |",
         "| `CurrentStepTypeFetcher` | `Func<string?>?` | `null` | Has no effect: nothing in this package reads it (since 4.12.1) |"),
        ("With `PropagateTestIdentity`, a produced message carries the scenario its produce is recorded under |",
         f"With `PropagateTestIdentity`, a message produced inside a scenario carries that scenario, the one its produce is recorded under, whichever thread the delivery report arrives on ({V}+); one produced outside any scenario carries none |"),
        ("| **Option G** (Build Interception) | **None** | Not required | Zero-change tracking of internally-built consumers/producers |",
         "| **Option G** (Build Interception) | **None** (the test project sets `<TieredCompilationQuickJit>false</TieredCompilationQuickJit>`) | Not required | Zero-change tracking of internally-built consumers/producers |"),
        ("This approach intercepts `ConsumerBuilder<TKey,TValue>.Build()` and `ProducerBuilder<TKey,TValue>.Build()` at runtime using [Harmony](https://github.com/pardeike/Harmony). When enabled, any call to `.Build()` — anywhere in the process — automatically returns a `TrackingKafkaConsumer` / `TrackingKafkaProducer` wrapping the real instance.",
         "This approach intercepts `ConsumerBuilder<TKey,TValue>.Build()` and `ProducerBuilder<TKey,TValue>.Build()` at runtime using [Harmony](https://github.com/pardeike/Harmony). When enabled, any call to `.Build()` for an enabled `<TKey,TValue>` pair, anywhere in the process, returns a `TrackingKafkaConsumer` / `TrackingKafkaProducer` wrapping the real instance; every other pair gets the client Confluent's own `Build()` makes."),
        ("""// Disable a specific type pair (patch remains but becomes a no-op):
KafkaBuildInterceptor.DisableConsumerTracking<string, string>();
KafkaBuildInterceptor.DisableProducerTracking<string, string>();

// Full cleanup — clears all tracking state and removes all Harmony patches:
KafkaBuildInterceptor.Reset();
```

> **Important:** Always call `KafkaBuildInterceptor.Reset()` in test teardown (e.g. `Dispose()`) to clean up Harmony patches between tests.""",
         f"""// Stop tracking one type pair: its builders return Confluent's own client again
KafkaBuildInterceptor.DisableConsumerTracking<string, string>();
KafkaBuildInterceptor.DisableProducerTracking<string, string>();

// Stop tracking every pair:
KafkaBuildInterceptor.Reset();
```

From {V}, the patches stay for the life of the process: `Reset()` and `Disable…()` clear tracking only, and a builder of a pair that is not enabled builds exactly what Confluent's `Build()` builds. Call `Reset()` in teardown when a later test must see untracked clients. (Before {V}, `Reset()` removed the patches, which could not restore the code other pairs share; see *How it works internally*.)"""),
        ("""1. `EnableConsumerTracking<TKey,TValue>()` registers tracking state in the base `KafkaTrackingInterceptor` (same as Option E)
2. It then applies a Harmony postfix patch on `ConsumerBuilder<TKey,TValue>.Build()` specifically for that generic type combination
3. The postfix calls `KafkaTrackingInterceptor.WrapConsumer()` on the build result — the same wrapping logic used by `.BuildTracked()`
4. `WrapConsumer()` checks for double-wrapping, so using `KafkaBuildInterceptor` together with `.BuildTracked()` is harmless""",
         f"""1. `EnableConsumerTracking<TKey,TValue>()` registers tracking state in the base `KafkaTrackingInterceptor` (same as Option E), one tracker per pair
2. It makes sure `ConsumerBuilder<,>.Build()` is patched for the pair's *code shape*. The runtime compiles one body of `Build()` for every pair whose arguments are reference types (`<string,string>`, `<Null,string>`, `<string,byte[]>`) and one for every pair with the same value-type arguments (`<int,string>`, `<int,byte[]>`), so a patch on one pair's `Build()` runs for all of them. Kronikol installs one patch per shape, under a lock, before `Enable…` returns
3. The patch builds the builder's own client through Confluent's constructor, so each pair gets a client of its own type and a builder Confluent refuses throws exactly what `Build()` throws, then calls `KafkaTrackingInterceptor.WrapConsumer()` on it, the same wrapping `.BuildTracked()` uses, which wraps only an enabled pair's client
4. `WrapConsumer()` checks for double-wrapping, so using `KafkaBuildInterceptor` together with `.BuildTracked()` is harmless

If the installed Confluent.Kafka has no such constructor, `Enable…()` throws `NotSupportedException` naming the version and the alternatives (`.Tracked()`, `.BuildTracked()`, the `Add…TestTracking` registrations) instead of patching.

**Tiered compilation.** The runtime can recompile a patched `Build()` after its first few dozen calls (about 40 in a test run with pauses between tests), and every client built after that is untracked. Add this to the test project:

```xml
<PropertyGroup>
  <TieredCompilationQuickJit>false</TieredCompilationQuickJit>
</PropertyGroup>
```

or set `DOTNET_TC_QuickJit=0` (or `DOTNET_TieredCompilation=0`) in the test environment; each held 20,000 of 20,000 builds. The environment variable wins over the project setting, and is read as a hexadecimal number, so `DOTNET_TC_QuickJit=false` turns quick JIT *on*. From {V}, `Enable…()` checks these settings as the runtime reads them and, where the patch can be lost, writes one line to standard error and adds a `CaptureDegraded` entry to the report's diagnostics. The cost is a slower start of each test process: measured on Kronikol's own Kafka tests (126 facts), 1.66 s became 2.28 to 2.54 s."""),
        ("- Different `<TKey, TValue>` combinations are independent — patching `<string, string>` does not affect `<Guid, string>`",
         f"- Different `<TKey, TValue>` combinations are independent from {V}: enabling `<string, string>` gives every other pair its own untracked client. (Before {V}, enabling one pair made the other pairs of its code shape return a client of the enabled pair, which threw `EntryPointNotFoundException` on first use.)"),
        ("1. **Producer side:** `TrackingKafkaProducer` injects two headers into every outgoing message:",
         f"1. **Producer side:** `TrackingKafkaProducer` adds two headers to each message produced inside a scenario (from {V}, a message produced outside any scenario gets none, and a header the message already has is kept as it is):"),
        ("Headers are stored as UTF-8 byte arrays in Confluent.Kafka's `Headers` collection:",
         f"Headers are stored as UTF-8 byte arrays in Confluent.Kafka's `Headers` collection. From {V} they are added to a copy of your `Message` that is handed to Confluent, never to the `Message` you passed, so a message you produce twice does not collect a pair per produce:"),
    ],
    'API-Reference.md': [
        ("| `KafkaTrackingOptions` | Record: `ServiceName`, `CallerName`, `Verbosity`, `CurrentTestInfoFetcher?`, `CurrentStepTypeFetcher?`, `TrackProduce`, `TrackConsume`, `TrackSubscribe`, `TrackCommit`, `LogMessageKey`, `LogMessageValue`, phase-aware properties. |",
         "| `KafkaTrackingOptions` | Class: `ServiceName`, `CallerName`, `Verbosity`, `CurrentTestInfoFetcher?`, `CurrentStepTypeFetcher?` (no effect), `TrackProduce`, `TrackConsume`, `TrackSubscribe`, `TrackUnsubscribe`, `TrackCommit`, `TrackFlush`, `TrackTransactions`, `LogMessageKey`, `LogMessageValue`, `PropagateTestIdentity`, `AutoCorrelateOnConsume`, `ConsumeKeyExtractor?`, `HttpContextAccessor?` (4.11.0+), and the phase-aware `SetupVerbosity`, `ActionVerbosity`, `TrackDuringSetup`, `TrackDuringAction`. |"),
        ("Both auto-resolve `IHttpContextAccessor` from DI. |",
         "`AddKafkaConsumerFactoryTestTracking<TKey,TValue>(options?)` and `AddKafkaProducerFactoryTestTracking<TKey,TValue>(options?)` register the default factory if none exists and decorate it. Each uses the container's `IHttpContextAccessor` when the options carry none. |"),
        ("| `KafkaOperation` | Enum: `Produce`, `ProduceAsync`, `Consume`, `Subscribe`, `Unsubscribe`, `Commit`, `Flush`, `Other`. |",
         "| `KafkaOperation` | Enum: `Produce`, `ProduceAsync`, `Consume`, `Subscribe`, `Unsubscribe`, `Commit`, `Flush`, `InitTransactions`, `BeginTransaction`, `CommitTransaction`, `AbortTransaction`, `SendOffsetsToTransaction`, `Other`. |\n"
         "| `KafkaTrackingInterceptor` | Static class: `EnableConsumerTracking`/`EnableProducerTracking<TKey,TValue>(options, accessor?)`, `Disable…`, `Reset()`; the state `.BuildTracked()` and `.Tracked()` read. |\n"
         "| `KafkaBuildInterceptor` | Static class in `Kronikol.Extensions.Kafka.BuildInterception`: the same `Enable…`/`Disable…`/`Reset()`, plus `EnableTracking<TKey,TValue>`, with `Build()` itself intercepted. The patches stay for the process. |"),
    ],
    'Background-Thread-Correlation.md': [
        ("The Kafka, Service\n   Bus and Event Hubs wrappers call it before they establish the next message's identity,",
         f"The Kafka consumer calls it\n   at every poll, whatever the poll returns ({V}+: before, an empty poll or a partition end kept the\n   last message's identity), the Service Bus and Event Hubs wrappers before they establish the next message's identity,"),
    ],
    'Generated-Reports.md': [
        ("The exception's type is `statusText` behind a bang (`!HttpRequestException`) and `statusCode` is `null`.",
         f"The exception's type is `statusText` behind a bang (`!HttpRequestException`) and `statusCode` is `null`; for Kafka ({V}+) it is Confluent's error code behind a bang (`!Local_MsgTimedOut`), for a failed delivery report too."),
        ("Repeated on both halves of a pair; `null` when the request went unanswered. |",
         f"Repeated on both halves of a pair; `null` when the request went unanswered, and when both halves carry one instant because the call was recorded after the fact (`LogPair` since 4.7.3; a Kafka consume, subscribe, commit, flush or transaction entry since {V}, while a produce measures its broker round trip). |"),
    ],
    'Exporting-to-OpenTelemetry.md': [
        ("or when it is a call that threw, the exception's type behind a bang (`!HttpRequestException`), whose status message is then the call's `error`",
         f"or when it is a call that threw, the exception's type behind a bang (`!HttpRequestException`; for Kafka, {V}+, Confluent's error code, `!Local_MsgTimedOut`), whose status message is then the call's `error`"),
    ],
    'Querying-Reports.md': [
        ("Follows a W3C trace id — the `activityTraceId` every interaction carries since 3.0.47, the same id in\nyour OTel traces and application logs — across the whole run.",
         "Follows a W3C trace id (the `activityTraceId` recorded since 3.0.47 on HTTP and gRPC calls, messages through\n`MessageTracker`, OTLP imports, ProxyTap and TcpTap captures, and a tracking proxy logging deferred with an\n`ActivitySourceName`; the same id as in your OTel traces and application logs) across the whole run."),
    ],
    'Integration-OpenTelemetry-Extension.md': [
        ("The W3C trace ids that flow through either path end up on every interaction in `TestRunReport.json`",
         "The W3C trace ids that flow through either path end up on the interactions that record one in `TestRunReport.json`\n(HTTP and gRPC calls, messages through `MessageTracker`, OTLP imports, ProxyTap and TcpTap captures, and a tracking proxy logging deferred with an `ActivitySourceName`)"),
    ],
    'Event-Driven-Architecture-Testing.md': [
        ("services.AddKafkaTracking(new KafkaTrackingOptions\n{\n    PropagateTestIdentity = true,\n    CallerName = \"My Service\",\n    ServiceName = \"Kafka\",\n    CurrentTestInfoFetcher = CurrentTestInfo.Fetcher,\n});",
         "services.AddKafkaConsumerTestTracking<string, string>(options =>\n{\n    options.PropagateTestIdentity = true;\n    options.CallerName = \"My Service\";\n    options.ServiceName = \"Kafka\";\n    options.CurrentTestInfoFetcher = CurrentTestInfo.Fetcher;\n});"),
    ],
    'Parallel-Safe-Background-Correlation.md': [
        ("services.AddKafkaTracking(new KafkaTrackingOptions\n{\n    AutoCorrelateOnConsume = true,  // Stores messageKey → testIdentity\n});",
         "services.AddKafkaConsumerTestTracking<string, string>(options =>\n{\n    options.AutoCorrelateOnConsume = true;  // Stores messageKey → testIdentity\n});"),
    ],
}

problems = 0
for page, edits in EDITS.items():
    path = f'{WIKI}/{page}'
    with open(path, encoding='utf-8', newline='') as f:
        text = f.read()
    for old, new in edits:
        count = text.count(old)
        if count != 1:
            problems += 1
            print(f'{page}: {count} matches for {old[:70]!r}')
            continue
        text = text.replace(old, new)
    if not CHECK:
        with open(path, 'w', encoding='utf-8', newline='') as f:
            f.write(text)
print('problems:', problems)
sys.exit(1 if problems else 0)
