"""R2's wiki edits (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 6.4), applied after the release is published,
on a wiki that already has R0's, F25's and R1's (r0_wiki.py, f25_wiki.py, r1_wiki.py).

Usage: python r2_wiki.py <wiki-worktree> <version> [--check]
Every `old` must occur exactly once; --check reports without writing. The pages are LF.
"""
import sys

WIKI, VERSION = sys.argv[1], sys.argv[2]
CHECK = '--check' in sys.argv
V = VERSION

KAFKA = 'Integration-Kafka-Extension'

PROCESSING = f"""## Tracing Message Processing Without Code Changes

`TrackConsume`'s scope ([[Decorating Your Own Kafka Client|{KAFKA}#decorating-your-own-kafka-client]]) needs a line in the consuming code. Where that code cannot change, `KafkaTrackingOptions.TraceMessageProcessing` ({V}+, default `false`) has the wrappers do the same, best effort:

```csharp
var options = new KafkaTrackingOptions {{ TraceMessageProcessing = true, /* ... */ }};
```

- **Consume** returns with the message's scope open: its `Kronikol.Kafka` span, `{{topic}} process`, is current, so the spans the handler starts are its children, and the consume is not answered yet.
- **The scope ends** at the consumer's next `Consume` (at its start, before it waits), `Commit`, `StoreOffset`, `Close` or `Dispose`. The consume is answered then, so its duration runs to that call, and the span current when that call began stays current; when that was the scope's own span, the span current before the scope takes its place. A span that has ended is never left current.
- **A produce** through the wrapper runs under a `{{topic}} publish` producer span. Where Kronikol adds the `traceparent` (its wrapper the last before Confluent's producer), the header names that span, so a consumer parents on a span that exists. The span ends when the produce call returns.

Where it falls short, and the scope does not:

- `await Task.Run(() => consumer.Consume(ct))` leaves nothing current for the code that awaits it: the span is current only inside the task. The consume is still answered at the next call, but the handler's spans are not the span's children.
- A `Commit` or `StoreOffset` from another thread, such as a commit timer, ends the window there, before the handler may have finished.
- A consumer that hands each message to a worker ends the window at its next poll, before the worker's later spans; `kronikol query trace` and the whole-test flow still have them.

The spans are sampled by Kronikol's listener, not recorded, so an app whose own tracing samples by parent drops the handler's child spans from its exports unless it listens to `Kronikol.Kafka` too.

---

## Wrapper Transparency"""

EDITS = {
    f'{KAFKA}.md': [
        ("---\n\n## Wrapper Transparency", PROCESSING),
        ("| `AutoCorrelateOnConsume` | `bool` | `true` |",
         f"| `TraceMessageProcessing` | `bool` | `false` | {V}+. The wrappers trace message processing with no change to the consuming code: `Consume` returns with the message's `Kronikol.Kafka` span current and the consume open until the consumer's next call, and a produce runs under a `{{topic}} publish` span. Best effort; see [[Tracing Message Processing Without Code Changes|{KAFKA}#tracing-message-processing-without-code-changes]] |\n| `AutoCorrelateOnConsume` | `bool` | `true` |"),
    ],
    'API-Reference.md': [
        ("`PropagateTraceContext` (4.17.0+), `AutoCorrelateOnConsume`,",
         f"`PropagateTraceContext` (4.17.0+), `TraceMessageProcessing` ({V}+), `AutoCorrelateOnConsume`,"),
    ],
    'Internal-Flow-Tracking.md': [
        ("- `Kronikol.Kafka` (4.17.0+: the span `KafkaTracker.TrackConsume` handles a message under)\n",
         f"- `Kronikol.Kafka` (4.17.0+: the span `KafkaTracker.TrackConsume` handles a message under; from {V} also the wrappers' spans under `TraceMessageProcessing`)\n"),
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
