# S8 and Q1: BreakfastProvider on R1's packages (2026-10-12)

A scratch clone of BreakfastProvider at `0ee43e8` (it pins 4.9.1), its 27 Kronikol references moved with
`plans/WARM_UP_PLAN.harness/breakfastprovider/bp_pin.py`, run with that harness's `runlanes.sh` (Debug build, then
`dotnet test` on the xUnit component lane, in memory). The scripts here carry the session's scratchpad paths.

- **Why the in-memory lane, not the docker lane the plan names.** The docker lane needs the Kafka certificates
  generated and installed in the machine's store and the hosts file edited (`docker/kafka-config/*.ps1`), which is not
  for a scratch run on the owner's machine. The in-memory lane still runs R1's wrapper: BreakfastProvider's
  `UseTrackedKafkaProducer` wraps its in-memory producer in `TrackingKafkaProducer`, and the app's own tracing
  (`AddSource("BreakfastProvider.Api")`) starts the `KafkaPublish` producer span around each produce
  (`KafkaEventPublisher.cs:62`). What it cannot show is the `traceparent` on a message, since its inner producer is not
  Confluent's; the broker facts cover that (`../broker-local-cp-kafka-7.6.1.txt`).
- **Probe.** `KafkaPublishProbe.cs`, dropped into the clone's xUnit project for the run and removed after, writes the
  ids of every `KafkaPublish` span to a file. It samples nothing itself, so which spans exist is the app's choice.
- **Match.** `match_ids.py` lists the Kafka Broker entries with `kronikol query interactions --json`, reads each one's
  `trace ... span ...` line with `kronikol query http`, and compares it with the probe's spans. The report is never
  opened.

## Results

| Run | Packages | Scenarios | Kafka produce entries | Carrying a `KafkaPublish` span's ids |
|---|---|---|---|---|
| `run-r1.sh` | 4.17.0 local, from `9aa01433` | 212 of 212 passed, 197 s | 40 (40 spans started) | **40** (`r1-match.txt`) |
| `run-control.sh` | 4.16.4 from nuget.org | 212 of 212 passed, 181 s | 40 (40 spans started) | **0**, no ids at all (`control-match.txt`) |

`r1-trace-sample.txt`: a produce entry's ids, and its W3C trace holding the `POST /muffins` that made it. The in-memory
consumer's `Consume (Kafka)` shares the produce's span because the fake broker hands the message over inside the
produce call.

Both runs print "3 calls carried a test id that no scenario of this run has". That is BreakfastProvider's own step code
scoping three calls under a request id, known since `INTERNAL_FLOW_BLOB_PLAN.md` F8 and in ROADMAP Appendix C; it is
not R1's.

## Q1: the cost of quick JIT off

`q1.sh`: the same lane on 4.17.0, three runs each way, alternated, `DOTNET_TC_QuickJit=0` for "off". Wall time of
`dotnet test` (`q1-summary.txt`):

| Quick JIT | Runs (s) | Mean (s) |
|---|---|---|
| on (default) | 169, 160, 177 | 169 |
| off | 183, 158, 182 | 174 |

A mean of +3%, inside the runs' own spread. The decision (no props in 4.x) is in the plan's section 11.
