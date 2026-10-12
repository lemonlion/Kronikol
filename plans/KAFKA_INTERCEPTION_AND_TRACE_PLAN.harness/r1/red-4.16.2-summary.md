# R1's facts on the tree published before R1, with compile stubs

Tree: `kafka-135-inline` at bc2c4a3d, which is 4.16.2 (8638a3d6) plus F25's test and doc fix, so its product code is
4.16.2's. Stubs: `stubs.py` (an option nothing reads, two constants, the IVTs R1's facts need, and `TrackProduce`,
`TrackConsume` and `KafkaProduceTracking` throwing `NotImplementedException`). R1's test files copied in with
`git checkout kafka-135-r1 -- <paths>`. Each fact below was written after the code it covers in S5 and S6, so this run
is the proof that it fails without that code.

## Kronikol.Tests.Kafka (`red-4.16.2-kafka-full.txt`)

48 fail, 231 pass, 3 skip.

- 42 fail on their own assertion: the trace-context facts (T24 to T26, T28, T30, T31, T47, T54, T57, T58's cases, the
  listener), the content facts (T59), and the facts R1 rewrote (the content assertions and identity-header lists now
  read the value as the body and carry `kronikol-trace-id`).
- 1 fails on its helper's assertion: `Query_trace_on_a_produces_trace_id_lists_the_produce_then_its_consume`, where
  `kronikol query trace` exits non-zero because no entry carries the trace (T28's query half).
- 5 fail by the stubs throwing, the plan's red for T61 ("the helpers do not exist"): the decorator facts that call
  `TrackProduce` or `TrackConsume`. The sixth decorator fact fails on its assertion (`Assert.Throws` meets the stub's
  exception in place of the produce's).
- 5 of R1's new facts pass, each a guard the plan names: a consume under another test than its message names records
  no ids (T55's consume half), nor one under the background, nor one whose message has no `traceparent` whatever span
  the loop runs in (T56); `Summarised` draws neither body nor headers; with `PropagateTraceContext` off no
  `traceparent` is added (T25's second half).
- The 3 broker facts skip: no Docker endpoint answers on this machine. CI's remainder lane runs them on ubuntu-latest.

T55's flow fact (`A_consume_under_another_test_leaves_the_producing_tests_flow_its_spans`) fails here though the plan
lists T55 as a guard on R0: its consume half holds on 4.16.2, but the producing test's flow takes the producer's spans
only through the ids R1's produce records, which 4.16.2's does not.

## Kronikol.Tests.Kafka.BuildInterception (`red-4.16.2-buildinterception.txt`)

`KafkaDependentProducerTests` on net8.0 and net10.0: 4 of 5 fail on their own assertion (T22, T23, the per-shape patch
count). The fifth, a dependent producer of a pair that is not enabled stays Confluent's own, is a guard and passes.

## Kronikol.Tests (`red-4.16.2-core.txt`)

`MessageKeyLineTests` and the scripts' label guard: 6 of 7 fail on their own assertion. The seventh, a note without a
key draws its headers first, is a guard and passes.

## Kronikol.Tests.EndToEnd

`red-4.16.2-e2e.txt`: the six `KafkaNoteTests` (T60) fail on their own assertion (the key line is not painted, or the
copied text differs), and `A_consume_handled_under_its_scope_opens_a_popup_of_the_handlers_spans` (T42) fails on its
first expectation, the popup: on 4.16.2 `Kronikol.Kafka` is no well-known source, so the default granularity drops
the consume's trace, its segment holds no span and the arrow is not bound. All seven pass on R1.
