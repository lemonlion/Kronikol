# One OTLP/JSON export, one trace per case, to answer "what does a backend's service graph do with these kinds": the spans Kronikol
# writes today and on R3 (flat: no parentSpanId and no links), and, for comparison, a producer and consumer joined as OTel means them
# to be. Each case's services are named after it, so an edge in a backend's answer says which case made it.
#
# Usage: python -I payload.py <out.json> <out2.json> <cases.tsv>. The second file is a second POST, for a span id repeated across two
# export batches, which is how a long test's spans reach a backend (OtlpExportSink batches them).
import json
import os
import sys
import time

now = time.time_ns()


def hex_id(length):
    return os.urandom(length // 2).hex()


def attrs(items):
    return [{"key": k, "value": {"stringValue": v}} for k, v in items.items()]


MESSAGING = {"messaging.system": "kafka", "messaging.operation.name": "send", "messaging.operation.type": "send", "messaging.destination.name": "orders"}
PROCESS = {"messaging.system": "kafka", "messaging.operation.name": "process", "messaging.operation.type": "process", "messaging.destination.name": "orders"}

batches = [{}, {}]
cases = []


def span(case, service, kind, attributes=None, trace=None, span_id=None, parent=None, links=None, duration_ms=10, batch=0):
    trace = trace or hex_id(32)
    span_id = span_id or hex_id(16)
    record = {
        "traceId": trace,
        "spanId": span_id,
        "name": case,
        "kind": kind,
        "startTimeUnixNano": str(now - duration_ms * 1_000_000),
        "endTimeUnixNano": str(now),
        "attributes": attrs(attributes or {}),
    }
    if parent:
        record["parentSpanId"] = parent
    if links:
        record["links"] = [{"traceId": t, "spanId": s} for t, s in links]
    batches[batch].setdefault(service, []).append(record)
    return trace, span_id


CLIENT, PRODUCER, CONSUMER = 3, 4, 5


def case(name, say, trace):
    cases.append((name, say, trace))


# Flat spans, as Kronikol exports.
t, _ = span("c1", "c1-app", CLIENT, {"peer.service": "c1-broker"})
case("c1", "CLIENT, peer.service (an SQS send today; a pull on R3)", t)
t, _ = span("c2", "c2-app", PRODUCER, {"peer.service": "c2-broker"})
case("c2", "PRODUCER, peer.service (a Pub/Sub publish today; an SQS send on R2+R3)", t)
t, _ = span("c3", "c3-app", PRODUCER, {"peer.service": "c3-broker", **MESSAGING})
case("c3", "PRODUCER, peer.service, messaging.* (a send on the first cut)", t)
t, _ = span("c4", "c4-app", CONSUMER, {"peer.service": "c4-broker", **PROCESS})
case("c4", "CONSUMER, the consumer as service.name, the broker as peer.service (a delivery on the first cut)", t)
t, _ = span("c5", "c5-broker", CONSUMER, {"peer.service": "c5-app"})
case("c5", "CONSUMER, the broker as service.name, the consumer as peer.service (a Kafka consume on section 5.5, unswapped)", t)
t, _ = span("c6", "c6-broker", PRODUCER, {"peer.service": "c6-app"})
case("c6", "PRODUCER, the broker as service.name, the consumer as peer.service (a Kafka consume today)", t)

# As OTel means them: a parent, then a link.
t, p = span("c7", "c7-producer", PRODUCER, MESSAGING)
span("c7", "c7-consumer", CONSUMER, PROCESS, trace=t, parent=p)
case("c7", "PRODUCER, then CONSUMER with it as parentSpanId (not what Kronikol writes)", t)
t, p = span("c8", "c8-producer", PRODUCER, MESSAGING)
t2, _ = span("c8", "c8-consumer", CONSUMER, PROCESS, links=[(t, p)])
case("c8", "PRODUCER, then CONSUMER in a trace of its own with a span link (not what Kronikol writes)", t)

# Database names (F17).
t, _ = span("c9", "c9-app", CLIENT, {"peer.service": "c9-peer", "db.system.name": "spanner"})
case("c9", "CLIENT, db.system.name spanner, peer.service", t)
t, _ = span("c10", "c10-app", CLIENT, {"db.system.name": "spanner"})
case("c10", "CLIENT, db.system.name spanner, no peer.service", t)
t, _ = span("c11", "c11-app", CLIENT, {"db.system.name": "gcp.spanner"})
case("c11", "CLIENT, db.system.name gcp.spanner, no peer.service", t)
t, _ = span("c12", "c12-app", PRODUCER, {"peer.service": "c12-peer", "db.system.name": "spanner"})
case("c12", "PRODUCER, db.system.name spanner, peer.service (Spanner at 5.27.13)", t)

# One span id twice. The spans differ in their times, as real ones do: a backend may drop a span that is byte for byte another.
t, s = span("c13", "c13-app", PRODUCER, {"peer.service": "c13-broker"})
span("c13", "c13-app", PRODUCER, {"peer.service": "c13-broker"}, trace=t, span_id=s, duration_ms=20)
span("c13", "c13-app", PRODUCER, {"peer.service": "c13-broker"}, trace=t, span_id=s, duration_ms=30)
case("c13", "three PRODUCER spans with one trace id and one span id (three sends under one Activity)", t)
t, s = span("c14", "c14-app", PRODUCER, {"peer.service": "c14-broker"})
span("c14", "c14-broker", PRODUCER, {"peer.service": "c14-app"}, trace=t, span_id=s, duration_ms=20)
case("c14", "a Kafka produce and its consume as they export today: PRODUCER twice, one span id", t)
t, s = span("c15", "c15-app", PRODUCER, {"peer.service": "c15-broker"})
span("c15", "c15-app", CONSUMER, {"peer.service": "c15-broker"}, trace=t, span_id=s, duration_ms=20)
case("c15", "a Kafka produce and its consume on the first cut: PRODUCER and CONSUMER, one span id", t)
t, s = span("c16", "c16-app", PRODUCER, {"peer.service": "c16-broker"})
span("c16", "c16-broker", PRODUCER, {"peer.service": "c16-app"}, trace=t, span_id=s, duration_ms=20, batch=1)
case("c16", "as c14, the second span in a second POST", t)
t, s = span("c17", "c17-app", PRODUCER, {"peer.service": "c17-broker"})
span("c17", "c17-app", CONSUMER, {"peer.service": "c17-broker"}, trace=t, span_id=s, duration_ms=20, batch=1)
case("c17", "as c15, the second span in a second POST", t)
t, s = span("c18", "c18-app", CLIENT, {"peer.service": "c18-peer"})
span("c18", "c18-app", CLIENT, {"peer.service": "c18-peer"}, trace=t, span_id=s, duration_ms=20, batch=1)
case("c18", "two CLIENT calls under one Activity, the second in a second POST", t)

for index, batch in enumerate(batches):
    payload = {
        "resourceSpans": [
            {
                "resource": {"attributes": attrs({"service.name": service})},
                "scopeSpans": [{"scope": {"name": "Kronikol"}, "spans": spans}],
            }
            for service, spans in batch.items()
        ]
    }
    with open(sys.argv[1 + index], "w", encoding="utf-8", newline="\n") as out:
        json.dump(payload, out)
with open(sys.argv[3], "w", encoding="utf-8", newline="\n") as out:
    for name, say, trace in cases:
        out.write(f"{name}\t{trace}\t{say}\n")
