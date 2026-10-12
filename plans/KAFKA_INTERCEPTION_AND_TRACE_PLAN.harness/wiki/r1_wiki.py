"""R1's wiki edits (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 6.4), applied after the release is published,
on a wiki that already has R0's (r0_wiki.py) and F25's (f25_wiki.py).

Usage: python r1_wiki.py <wiki-worktree> <version> [--check]
Every `old` must occur exactly once; --check reports without writing. The pages are LF.
"""
import sys

WIKI, VERSION = sys.argv[1], sys.argv[2]
CHECK = '--check' in sys.argv
V = VERSION

KAFKA = 'Integration-Kafka-Extension'

MESSAGE_CONTENT = f"""## Message Content

From {V} a Kafka note shows a message the way an HTTP note shows a request: the value is the body, and the key and the message's headers are the entry's headers.

- **Value**: the body, when `LogMessageValue = true` (default). A JSON value is indented, offered the YAML view and read by `kronikol query` as an HTTP body is.
- **Key**: the entry's `kafka-key` header, when `LogMessageKey = true` (default). The note draws it first, on a line of its own (**Key** order-123), and keeps it in view when the header toggle hides the headers, as it does by default since 4.0.0.
- **Headers**: the message's own headers, drawn below the key in header ink and shown or hidden with the toggle. A produce records the headers the caller's message carried; a consume leaves out the `kronikol-*` headers Kronikol adds.
- **Summarised**: no body and no headers.

| Key or value | Written as |
|---|---|
| `string` | As it is |
| `byte[]` | As a binary HTTP body is: UTF-8 text unless the bytes are binary, else the JSON documents they hold, else `[binary content]` |
| A number, `bool`, `Guid`, `DateTime` or `DateTimeOffset` | Its invariant text, a date in the round-trip `O` format |
| A Google.Protobuf `IMessage` | Its JSON, from `JsonFormatter.Default` (found by reflection: the package takes no reference on Google.Protobuf) |
| Anything else | JSON, as the other trackers serialise an object |
| `null`, `Null` or `Ignore` | Nothing |

A header's value is its bytes as UTF-8 text when they decode as UTF-8, else `[binary content]`. The note cuts a key longer than 120 characters and draws its line breaks as spaces; the report's data keeps the key as it was.

A field of a JSON message is queryable as a request body's is:

```bash
kronikol query values <report> --service Kafka --request --path '$.Status'
```

A value cut at `RequestResponseLogger.MaxContentLength` is no longer JSON, so it gets no YAML view and `values` reads nothing from it, as for an HTTP body.

Before {V} the content was one line, `Key: order-123, Value: {{"amount":99}}`."""

DEPENDENT_PRODUCERS = f"""

### Dependent Producers

A library that shares one connection between producers of several types builds them with `new DependentProducerBuilder<TKey,TValue>(owner.Handle).Build()`. From {V}, `EnableProducerTracking<TKey,TValue>` (and `EnableTracking`) tracks those producers as it tracks `ProducerBuilder`'s, for each enabled pair. A dependent producer of a pair that is not enabled is not tracked. Confluent reads a dependent producer's delivery-report setting from its owner, so one whose owner turned delivery reports off is logged at the call, without a partition or offset.
"""

DECORATING = f"""## Decorating Your Own Kafka Client

A library that builds its Kafka clients behind interfaces of its own can be neither wrapped by `TrackingKafkaProducer` nor reached by build interception. From {V}, `KafkaTracker` gives a decorator of such a client what the wrappers do:

```csharp
public interface IOrderPublisher
{{
    Task<DeliveryResult<string, string>> PublishAsync(string topic, Message<string, string> message);
}}

public sealed class TrackedOrderPublisher(IOrderPublisher inner, KafkaTracker tracker) : IOrderPublisher
{{
    public async Task<DeliveryResult<string, string>> PublishAsync(string topic, Message<string, string> message)
    {{
        var produce = tracker.TrackProduce(topic, message, KafkaOperation.ProduceAsync);
        try
        {{
            var result = await inner.PublishAsync(topic, produce.Message);
            produce.Delivered(result);
            return result;
        }}
        catch (Exception exception)
        {{
            produce.Failed(exception);
            throw;
        }}
    }}
}}
```

`TrackProduce` takes the scenario, phase, verbosity, time and trace ids at the call, so the produce is its caller's whichever thread reports the outcome. Hand Kafka `produce.Message`, never the caller's message: it is a copy carrying the identity headers, the Kronikol trace id and, with `PropagateTraceContext` on and none on the message, a `traceparent` (turn the option off when the client you decorate writes its own). Then report the outcome once: `Delivered(DeliveryResult)`, `Delivered(DeliveryReport)` or `produce.Handler(then)` passed as a delivery handler, `Failed(exception)`, or `Sent()` for a producer with delivery reports off. A second report is ignored.

On the consuming side:

```csharp
var result = consumer.Consume(cancellationToken);
using (tracker.TrackConsume(result))
{{
    await HandleAsync(result.Message);
}}
```

`TrackConsume` returns the scope the message is handled in. It makes the scenario the message's identity headers name current and stores the key's correlation; logs the consume's request when it starts and its response when it is disposed, so the calls the handler makes lie inside the consume (`kronikol query flow` nests them under it) and the consume's duration is the time the message took to handle; and runs the handler under a `Kronikol.Kafka` consumer span, `{{topic}} process`, whose ids the consume records, so the handler's spans are the span's children and show in the consume arrow's internal-flow popup. The span is a child of the message's `traceparent` when the consume is recorded under the test the message names, and the root of a trace of its own otherwise, never a child of the span the loop polls under; the span current before the scope is current again after it. Disposing ends the message's identity. A null result or a partition end tracks nothing.

`LogProduce` and `LogConsume` (content as the body), `LogSubscribe`, `LogUnsubscribe`, `LogCommit`, `LogFlush` and `LogTransaction` each log one instant under the scenario current at the call, with the trace ids of `Activity.Current`.

Interception and `KafkaTrackingInterceptor` are process-wide: one tracker and one set of options per key and value type, so in a process with several hosts every intercepted client of a pair is drawn with that pair's `CallerName`, whichever host built it. A decorator built per host, with a tracker of its own, draws each host under its own name.

---

## Wrapper Transparency"""

TRACE_IDS = f"""

---

## Trace Ids and `traceparent`

From {V}, Kafka entries record W3C trace and span ids (`activityTraceId`, `activitySpanId`), so `kronikol query trace` follows a message from its produce to its consume, and the internal-flow popups and the whole-test flow can take the spans of its trace:

| Entry | Ids recorded |
|---|---|
| A produce through the wrapper | The last `traceparent` of the message sent: the caller's own, one an instrumentation wrapper inside Kronikol's added, or Kronikol's (below). Else those of `Activity.Current` at the call |
| A consume through the wrapper | The consumed message's last `traceparent`, and only when the consume is recorded under the test the message's identity headers name. Otherwise none, and never the polling loop's `Activity.Current`, which every consume of every test would share |
| `LogProduce` or `LogConsume` called by hand | Those of `Activity.Current` at the call |
| `TrackProduce` | As a produce through the wrapper |
| `TrackConsume` | Those of the `Kronikol.Kafka` span its scope handles the message under: a child of the message's last `traceparent` when the consume is recorded under the test the message names, else the root of a trace of its own ([[Decorating Your Own Kafka Client|{KAFKA}#decorating-your-own-kafka-client]]) |

A consume's `activitySpanId` through the wrapper is therefore the producer's span, not one of its own; `TrackConsume`'s is its own span's.

**Adding `traceparent`.** With `PropagateTraceContext` (default `true`), a message produced without a `traceparent` is handed to Confluent with one naming `Activity.Current`: flags `01` when the span is recorded, `00` when it is not (as it is not where only Kronikol listens), and new ids with flags `00` when no span is current. Kronikol adds one only where its wrapper is the last before Confluent's own producer. When another wrapper sits inside it, an instrumentation package's (the usual shape when `AddKafkaProducerTestTracking` decorates a registered producer), that wrapper writes the header and Kronikol records it. A `traceparent` the caller set is kept, and the caller's `Message` is never written to.

**Parsing.** A header is read as the runtime reads one when it parents a span (`ActivityContext.TryParse`): an upper-case id, version `ff`, a zero id or text after the flags makes it count as absent.

**The Kronikol trace id.** A message produced inside a scenario also carries its produce's Kronikol trace id (the report's `traceId`) in a `kronikol-trace-id` header, under `PropagateTestIdentity`. When a host produces it while serving a test's request, that is the request's trace id, and a consume under the message's test takes it, so `kronikol query flow` nests the delivery under the call that published it while that call is open.

**Spans.** A Kafka tracker starts Kronikol's activity listener on its first call, as the HTTP handler does, so a suite that tracks only Kafka captures spans too. A consumer whose processing span is parented on the message (as `Confluent.Kafka.Extensions.Diagnostics` does) puts the handler's spans in the producing test's whole-test flow; one that links to it instead (as OpenTelemetry's contrib package does) does not.
"""

EDITS = {
    f'{KAFKA}.md': [
        ("for alternative approaches including `MessageTracker` and `TrackingProxy<T>`.",
         f"for alternative approaches including `MessageTracker` and `TrackingProxy<T>`. A library that builds its producers with `DependentProducerBuilder` is covered by build interception from {V} ([[Dependent Producers|{KAFKA}#dependent-producers]]), and a decorator of a library's own client can track it with `KafkaTracker.TrackProduce` and `TrackConsume` ([[Decorating Your Own Kafka Client|{KAFKA}#decorating-your-own-kafka-client]])."),
        ("| **Raw** | `ProduceAsync orders-topic[2]@42` | `kafka:///orders-topic/2@42` | Key + Value |",
         f"| **Raw** | `ProduceAsync orders-topic[2]@42` | `kafka:///orders-topic/2@42` | Value as the body, key and headers ([[Message Content|{KAFKA}#message-content]]) |"),
        ("| **Detailed** | `Produce → orders-topic` | `kafka:///orders-topic` | Key + Value |",
         f"| **Detailed** | `Produce → orders-topic` | `kafka:///orders-topic` | Value as the body, key and headers |"),
        ("| `AutoCorrelateOnConsume` | `bool` | `true` |",
         f"| `PropagateTraceContext` | `bool` | `true` | {V}+. A produced message that carries no `traceparent` is given one naming `Activity.Current` (new ids, not sampled, when no span is current), so a consumer that parents on the message joins the producer's trace. Only where Kronikol's wrapper is the last before Confluent's own producer. See [[Trace Ids and traceparent|{KAFKA}#trace-ids-and-traceparent]] |\n| `AutoCorrelateOnConsume` | `bool` | `true` |"),
        ("""## Consumer Content

Both the producer and consumer wrappers build diagram content consistently:

- **Key** — Included when `LogMessageKey = true` (default). Shown as `Key: <value>`
- **Value** — Included when `LogMessageValue = true` (default). Shown as `Value: <value>`
- **Summarised mode** — Content is suppressed entirely regardless of key/value settings

Example content at Detailed verbosity with both enabled: `Key: order-123, Value: {"amount":99}`""",
         MESSAGE_CONTENT),
        ("This approach intercepts `ConsumerBuilder<TKey,TValue>.Build()` and `ProducerBuilder<TKey,TValue>.Build()` at runtime",
         f"This approach intercepts `ConsumerBuilder<TKey,TValue>.Build()`, `ProducerBuilder<TKey,TValue>.Build()` and, from {V}, `DependentProducerBuilder<TKey,TValue>.Build()` at runtime"),
        ("- `DisableConsumerTracking`/`DisableProducerTracking` are independent — disabling consumer tracking does not affect producer tracking\n",
         "- `DisableConsumerTracking`/`DisableProducerTracking` are independent — disabling consumer tracking does not affect producer tracking\n" + DEPENDENT_PRODUCERS),
        ("## Wrapper Transparency", DECORATING),
        ("1. **Producer side:** `TrackingKafkaProducer` adds two headers to each message",
         "1. **Producer side:** `TrackingKafkaProducer` adds these headers to each message"),
        ("   - `kronikol-test-id` — the unique test identifier\n",
         f"   - `kronikol-test-id` — the unique test identifier\n   - `kronikol-trace-id`: from {V}, the Kronikol trace id of the produce, so the consume joins its call ([[Trace Ids and traceparent|{KAFKA}#trace-ids-and-traceparent]])\n"),
        ("| `kronikol-test-id` | UTF-8 bytes of the test ID |",
         f"| `kronikol-test-id` | UTF-8 bytes of the test ID |\n| `kronikol-trace-id` | UTF-8 bytes of the produce's Kronikol trace id ({V}+) |"),
        ("See [[Background Thread Correlation › Solution 1b: Automatic Message Header Propagation (v2.34.0+)|Background-Thread-Correlation#solution-1b-automatic-message-header-propagation-v2340]] for the full architecture overview.\n",
         "See [[Background Thread Correlation › Solution 1b: Automatic Message Header Propagation (v2.34.0+)|Background-Thread-Correlation#solution-1b-automatic-message-header-propagation-v2340]] for the full architecture overview.\n" + TRACE_IDS),
    ],
    'API-Reference.md': [
        ("| `KafkaTracker` | Central logging helper for Kafka operations. Implements `ITrackingComponent`. Constructor: `(KafkaTrackingOptions, IHttpContextAccessor?)`. |",
         f"| `KafkaTracker` | Central logging helper for Kafka operations. Implements `ITrackingComponent`. Constructor: `(KafkaTrackingOptions, IHttpContextAccessor?)`. `LogProduce`/`LogConsume(KafkaOperationInfo, string? content)` and `LogSubscribe`, `LogUnsubscribe`, `LogCommit`, `LogFlush`, `LogTransaction(KafkaOperationInfo)` log one instant each; from {V}, `TrackProduce<TKey,TValue>(topic or TopicPartition, Message<TKey,TValue>, KafkaOperation = Produce)` returns a `KafkaProduceTracking<TKey,TValue>`, and `TrackConsume<TKey,TValue>(ConsumeResult<TKey,TValue>?)` returns the `IDisposable` scope a message is handled in, under a `Kronikol.Kafka` span, the consume answered when it is disposed. |\n| `KafkaProduceTracking<TKey,TValue>` | {V}+. One produce being tracked: `Message` (the message to hand to Kafka), `Delivered(DeliveryResult)`, `Delivered(DeliveryReport)`, `Failed(Exception)`, `Sent()`, `Handler(Action<DeliveryReport>? then)`. Reports its outcome once. |"),
        ("`LogMessageKey`, `LogMessageValue`, `PropagateTestIdentity`, `AutoCorrelateOnConsume`,",
         f"`LogMessageKey`, `LogMessageValue`, `PropagateTestIdentity`, `PropagateTraceContext` ({V}+), `AutoCorrelateOnConsume`,"),
        ("with `Build()` itself intercepted.",
         f"with `Build()` itself intercepted (`DependentProducerBuilder`'s too, from {V})."),
        ("and by `GrpcTrackingInterceptor` on its first tracked call. Captures",
         f"by `GrpcTrackingInterceptor` on its first tracked call, and from {V} by a Kafka tracker on its first call. Captures"),
    ],
    'Internal-Flow-Tracking.md': [
        ("- `Kronikol.Grpc`\n- `Grpc.Net.Client`\n",
         f"- `Kronikol.Grpc`\n- `Grpc.Net.Client`\n- `Kronikol.Kafka` ({V}+: the span `KafkaTracker.TrackConsume` handles a message under)\n"),
        ("record `Activity.Current?.TraceId`, `Activity.Current?.SpanId`, and `DateTimeOffset.UtcNow` on every request and response log entry.",
         f"record `Activity.Current?.TraceId`, `Activity.Current?.SpanId`, and `DateTimeOffset.UtcNow` on every request and response log entry. From {V} Kafka entries record ids too: a produce those of its message's `traceparent` or of `Activity.Current`, a consume those of the message's `traceparent`, so a consume's span id is the producer's ([[Trace Ids and traceparent|{KAFKA}#trace-ids-and-traceparent]])."),
        ("and by `GrpcTrackingInterceptor` on its first tracked call.",
         f"by `GrpcTrackingInterceptor` on its first tracked call, and from {V} by a Kafka tracker on its first call."),
        ("The `TestTrackingMessageHandler` auto-starts an `InternalFlowActivityListener` for well-known sources.",
         f"The `TestTrackingMessageHandler` auto-starts an `InternalFlowActivityListener` for well-known sources, as a Kafka tracker does from {V}."),
    ],
    'Generated-Reports.md': [
        ("Consumed by `kronikol query trace`, which follows one id across the whole run and flags an id leaking across scenarios. |",
         f"Consumed by `kronikol query trace`, which follows one id across the whole run and flags an id leaking across scenarios. A Kafka consume's `activitySpanId` ({V}+) is the span of the producer that sent the message, read from its `traceparent`, not a span of its own. |"),
    ],
    'Ingesting-External-Captures.md': [
        ("otherwise they cross-link to Tempo or Jaeger. Read lowercased, an empty one as none (4.4.0). |",
         f"otherwise they cross-link to Tempo or Jaeger. Read lowercased, an empty one as none (4.4.0). For a consumed message, send the producer's ids from its `traceparent`, as Kronikol's own Kafka consumes record them from {V}. |"),
    ],
    'Querying-Reports.md': [
        ("messages through\n`MessageTracker`, OTLP imports,",
         f"messages through\n`MessageTracker`, Kafka produces and consumes ({V}+), OTLP imports,"),
        ("is the classic flaky-test smell, and nothing else in the tool can see it.",
         "is the classic flaky-test smell, and nothing else in the tool can see it. A Kafka consume carries ids only under the test its message names, so it never spreads a trace; Kafka produces made under one long-lived span (a hosted service's loop started inside an `Activity`), or messages every test gives the same `traceparent`, put every scenario's produces on one trace, and there the warning means that span or header is shared."),
    ],
    'Integration-OpenTelemetry-Extension.md': [
        ("(HTTP and gRPC calls, messages through `MessageTracker`, OTLP imports,",
         f"(HTTP and gRPC calls, messages through `MessageTracker`, Kafka produces and consumes from {V}, OTLP imports,"),
    ],
    'Exporting-to-OpenTelemetry.md': [
        ("those spans land in the *same distributed trace* the system under test emitted, which is the whole point (invariant D4, below).",
         f"those spans land in the *same distributed trace* the system under test emitted, which is the whole point (invariant D4, below). Kafka entries record these from {V}, so exported Kafka spans moved from the per-test trace into the app's trace."),
    ],
    'Filtering-and-Redacting-Diagram-Content.md': [
        ("since truncation happens in the core `Log()` method.",
         f"since truncation happens in the core `Log()` method. A cut body is no longer JSON, so it gets no YAML view and `kronikol query values` reads nothing from it; that includes a Kafka message's value, the body of its note from {V}."),
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
            print(f'{page}: expected 1 occurrence, found {count}: {old[:90]!r}')
            continue
        text = text.replace(old, new)
    if not CHECK:
        with open(path, 'w', encoding='utf-8', newline='') as f:
            f.write(text)
print(f'problems: {problems}')
sys.exit(1 if problems else 0)
