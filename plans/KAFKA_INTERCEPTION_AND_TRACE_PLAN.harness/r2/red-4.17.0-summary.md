# R2's facts on the release before R2 (4.17.0), with the option stubbed

Tree: `kafka-135-r2`'s first commit (`e20f6349` after the rebase), which is 4.17.0 (`cfbbd862`) plus
`KafkaTrackingOptions.TraceMessageProcessing`, an option nothing reads, and R2's facts
(`KafkaMessageProcessingTests`, with `ScriptedConsumer.ReturnsFrom` in the fakes). The facts were written before the
code, so this is the order they were run in; re-run on the release once R1 shipped (2026-10-12).

## Kronikol.Tests.Kafka (`red-4.17.0-kafka.txt`)

11 fail, 297 pass, 3 skip (the broker facts: no Docker endpoint answers here and `KRONIKOL_TEST_KAFKA` was not set).

- 11 of R2's 15 fail on their own assertion: `Consume` returning with the message's span current and the consume not
  yet answered (T62's zero-change case); the next `Consume` answering it before it polls; each of the seven other next
  calls (`Commit` three ways, `StoreOffset` two ways, `Close`, `Dispose`) answering it and ending its span; a produce
  running under a `{topic} publish` span its `traceparent` names (T64); a loop that wraps each `Consume` in its own
  span never parenting one on a stopped span (T62).
- 4 pass, each a guard (`red-4.17.0-class-detailed.txt`): with the option off the wrappers start no span and answer a
  consume at once (T43); the deferred response keeps its request's test, phase and ids (T63, which an immediate answer
  meets too); two consumers in one loop never leave a stopped span current; a `Consume` inside `Task.Run` leaves the
  awaiting code its own span, never a stopped one (both T62). A guard cannot fail before the code; the mutations
  (`mutate.py`) show it fails when the code breaks it.
