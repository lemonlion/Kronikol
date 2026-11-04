# Harness: CALLS_AND_EVENTS_PLAN.md (#220)

The probes behind every RUN line of [`../CALLS_AND_EVENTS_PLAN.md`](../CALLS_AND_EVENTS_PLAN.md), with the output each one printed on
2026-11-02. Each probe is a file-based app: `dotnet run --file probes/<name>.cs` with the .NET 10 SDK (10.0.300, Windows 11). They take the
**published 5.27.13** packages from nuget.org, never the working tree, so they measure what a consumer has, and they build nothing in the
repository. `kronikol` below is `Kronikol.Tool` 5.27.13, installed with `dotnet tool install --tool-path <scratch>`. "The prototype" is
`prototype/r1-first-cut.patch` applied to `27cc0e46`, with its tool run as `dotnet Kronikol.Tool.dll`. The exceptions run on a prototype, and
say so: `<scratch>` in a probe or a result stands for the folder that held the throwaway worktree.

## The prototypes

| Patch | What it is |
|---|---|
| `prototype/r1-first-cut.patch` | R1: `ReadMethod`, `MethodText`, the three trackers, three flipped pins, the theories of plan §6.1. Applies to `27cc0e46` |
| `prototype/r2-first-cut.patch` | R2: 8 source files, one flipped pin, the theory of plan §6.7. Applies on R1 |
| `prototype/r2-option-warm-up-judges-publishes.patch` | Q5 (b), the one warm-up condition. Applies on R2. Not part of its patch |
| `prototype/r3-first-cut.patch` | R3 with Q6 (a), with F19's gate, without F17: 9 source files, 7 test files. Applies on R1, and on R1 and R2 |
| `prototype/r3-option-send-is-client.patch` | Q6 (b). Applies on R3. Not part of its patch |

## The probes

| Probe | What it measures | Output |
|---|---|---|
| `probes/history-across-merge.cs`, `probes/history-verdicts.py` | Seven runs making identical calls, one ledger. Run seven's own report against the ledger, then `kronikol merge run7 --history <ledger>`, then the prototype's merge, with each scenario's verdict from the labs page, and the ledger's bytes before and after a merge | `results/history-across-merge.txt` |
| `probes/history-in-failures.cs` | The same for one scenario on a Cosmos DB `Delete`, for `Failures.md` and the CI summary. First block: run seven already in the ledger, as a local run does (F18). Then CI's order, with run seven not yet recorded: newly broken, passing throughout, still failing | `results/history-in-failures.txt` |
| `probes/merge-labels.cs` | One shard from the real Spanner and BigQuery trackers, plus the labels `verb-labels` found, through `kronikol merge` and the prototype's merge: `query interactions --group-by service,kind,method` on each | `results/merge-labels.txt` |
| `probes/verb-labels.cs` | Reflection over every database package's classifier: every operation at every verbosity, for labels a merge rewrites, and whether each needs a missing table or object name, and is at the package's default verbosity | `results/verb-labels.txt` |
| `probes/kinds-by-tracker.cs` | Every operation of every route of the nine broker packages, the three GCP packages and the core `MessageTracker`, behind the real SDK client where there is a handler: label, category, request and response kind, `MessageTable`'s verdict, OTLP span kind and database name. 202 rows | `results/kinds-by-tracker.tsv` |
| `probes/warm-up-replay.cs` | `WarmUpCalls.Find`, by reflection, over a real mergeable report as recorded and with its statement-shaped requests read as `Default` | `results/warm-up-replay-breakfastprovider.txt` |
| `probes/sweep-rerun.sh` | Runs the next five end to end, and writes their results | |
| `probes/history-routes.cs`, `probes/history-routes-names.txt`, `probes/history-routes-verdicts.py` | Seven identical runs through the routes F2's first list missed: Redis, Storage Queues, gRPC, `DispatchProxy`, verbs built by hand. The run's own view, its merge, and the prototype's merge | `results/history-routes.txt` |
| `probes/history-routes-on-r1.sh` | The same with its shards written, recorded and merged by R1's own library and tool, as `MethodText` is in the library | The last block of `results/history-routes.txt` |
| `probes/card-queue.cs`, `probes/card-queue-read.py` | `service.json` of a Storage Queues delete: the run's, its merge's, the prototype's merge's | `results/card-queue.txt` |
| `probes/otlp-verb.cs` | F19: a statement-shaped call whose label is a verb, as `kronikol export`'s NDJSON route maps it, beside the live route | `results/otlp-verb.txt` |
| `probes/history-ci.cs`, `probes/ci-run-ids.sh`, `probes/ci-run-ids-more.sh` | F18: seven runs under the environment variables of Actions, Azure DevOps and GitLab, and off CI, merged before and after `history record`. Simulated environments, not real runners | `results/ci-run-ids.txt` |
| `probes/f1-at-3.11.0.cs` | F1 on the published 3.11.0 package and tool, with labels logged by hand, and 3.10.0's `merge --help` | `results/f1-at-3.11.0.txt` |
| `probes/verb-routes.cs`, `probes/verb-others.cs`, `probes/verbcased.py` | The sweep behind F2: labels through real routes, reflection over the packages `verb-labels` left out, and verb-cased calls in a real fragment | Their outcome is F2 |
| `probes/merge-round-trip-fixture.cs`, `-cases.py`, `-rules.cs`, `-rules.py`, `-uris.cs`, `-krkl.cs`, `-ingest-null.py` | Does a merge change anything else history reads? Generated cases for every field, rule moves, consumer rules, identity and a URI fuzz, on 5.27.13 and on R1. C1 to C8 of F20 and F21 come from here | `results/merge-round-trip-cases-*.txt`, `-rules.txt`, `-uris-*.txt`, `-item7.txt` |
| `probes/merge-round-trip-real.cs`, `-fragment.cs`, `-jsondiff.py`, `-querydiff.py` | The same over real reports and their fragments, by reflection: every scenario's call lines. Counts and generic labels only | `results/merge-round-trip-real-*.txt`, `-querydiff.txt` |
| `probes/merge-round-trip-shards.sh`, `-bp-run.sh`, `-bp-bump.py` | A real consumer's lane in two shards, with and without the ledger visible at run time: C1 and C2 | `results/merge-round-trip-real-lane.txt` |
| `probes/mutate.py`, `probes/mutations_r1.py` | R1's facts on unfixed source, and does each of its changes have a fact that fails without it? | `results/r1-merged-history-red.txt`, `results/r1-mutations.txt` |
| `probes/mutate.py`, `probes/mutations_r3.py` | Does each of R3's changes have a fact that fails without it? | `results/r3-mutations.txt` |
| `probes/merge-round-trip-candidate-patch.py`, `-shards-candidate.sh` | Candidate fixes for F20 and F21, checked in a copy, never against the repository's suite | `results/merge-round-trip-candidate.txt` |
| `probes/warm-up-shapes.cs` | R2's warm-up table: 31 shapes through the real trackers, on today's build, R2 and R2 with Q5 (b) | `results/warm-up-shapes.txt`, `-5.27.13.txt`, `-r2.txt`, `-r2-option.txt` |
| `probes/warm-up-replay-r2.cs` | The real report's marks with R2's kinds applied, and on the option's real code | `results/warm-up-replay-r2-breakfastprovider.txt`, `results/warm-up-replay-r2-option-breakfastprovider.txt` |
| `probes/warmup-spec-cost.py`, `probes/warmup-parity-lane.py` | What `warmup.py` would need under Q5 (b): a port over every lane of its corpus, and a synthetic lane of test-made publishes against the parity fact | `results/warmup-spec-cost.txt` |
| `probes/failed-sends.cs` | What each package logs when a send throws, on 5.27.13 and R2. MassTransit through its real observers, with a proxy context, as its bus needs a licence | `results/failed-sends-5.27.13.txt`, `results/failed-sends-r2.txt` |
| `probes/relative-address-report.cs` | F25 and the first cut of Q5 (b): one whole report through a record with a relative address, one process per set of options | `results/relative-address-report.txt` |
| `probes/r2-citations.sh` | The lines R2's text cites, printed at `27cc0e46` and the wiki at `670b1fc` | `results/r2-citations.txt` |
| `probes/named-kinds.py` | Facts in the broker suites whose names state a kind, against what R2 makes them assert | `results/named-kinds.txt` |
| `probes/readers-of-kind.cs` | What each reader of the field does under R2: note card, collapse key, OTLP, protocol | `results/readers-of-kind-5.27.13.txt`, `-r2.txt` |
| `probes/on-prototype.sh`, `probes/kinds-r2-analyse.py` | The matrix on R2 at each verbosity, and what moved | `results/kinds-by-tracker-r2-*.tsv`, `results/kinds-r2-analysis.txt` |
| `probes/r3-span-kinds.cs` | R3's 194 rows through the real trackers: what §5.5's second draft gives read literally, what the patch gives, and an expected column written by hand from OTel's table | `results/r3-span-kinds.tsv`, `.txt` |
| `probes/r3-trace-id.cs` | F15 on 5.27.13, the first cut, and a variant pairing on the pair id | `results/r3-trace-id-5.27.13.txt`, `r3-trace-id.txt`, `r3-trace-id-keyed-on-pair-id.txt` |
| `probes/r3-way-back.cs`, `probes/r3-real-capture.cs`, `probes/r3-real-capture-kafka.sh` | F11: spans through `SpanToInteractionMapper` and back, hand-made and from a real Confluent.Kafka and MassTransit capture against a real broker | `results/r3-way-back.txt`, `results/r3-real-capture.txt` |
| `probes/r3-export-verbs.cs` | Kinds and verbs through `kronikol export` | `results/r3-export-verbs.txt` |
| `probes/r3-worse-kinds.cs` | Does R3 alone worsen any row? `kinds-by-tracker`'s 202 rows against a hand-written truth, in process and through NDJSON, and hand-logged shapes | `results/r3-worse-kinds.txt` |
| `probes/r3-fingerprint.cs` | Every line a ledger holds, on 5.27.13 and on R3, for 23 hand-made shapes and 8 real spans, by log, by record and merged | `results/r3-fingerprint-5.27.13.txt`, `r3-fingerprint.txt`, `r3-fingerprint-diff.txt` |
| `probes/r3-warm-up-tap.cs` | Warm-up marks on a Messaging-on tap's records: 5.27.13, R3 alone, R3 with Q5 (b) | `results/r3-warm-up-tap-5.27.13.txt`, `r3-warm-up-tap.txt`, `r3-warm-up-tap-with-q5.txt` |
| `probes/r3-kafka-creation-context.cs` | OTel's condition on a send: does the `traceparent` on a Kafka message equal the exported span, on each route and through a real broker? | `results/r3-kafka-creation-context.txt` |
| `probes/r3-backends/` | What the OpenTelemetry Collector 0.164.0, Tempo 2.10.10 and Jaeger 2.22.0 draw for each shape of span, in containers under podman | `results/r3-backends.txt` |
| `probes/proxy_stress.py` | F27: a built `Kronikol.Tests.ProxyTap` under a chosen pressure, copies at once, busy loops, programs churning ports, keeping each failure's message. `<built> <label> <instances> <rounds> <out> [--cpu N] [--churn N] [--filter]`. It runs a built tree, not the published packages | `results/f27-proxytap.txt` |
| `probes/mutations_r4.py`, `-r4b.py`, `-r4c.py`, `-r4d.py`, `probes/mutations_f27-all.txt` | R4's and F27's mutations: a change to the source each, and the facts that must fail | `results/r4-mutations.txt`, `results/f27-mutations.txt` |
| `probes/linux-dual-mode.cs` | What Linux does to a socket's dual mode at the bind, which is why a fact of 5.28.2 failed there. Run in the SDK's container, as `::` raises a firewall prompt on Windows | `results/r4-linux-dual-mode.txt` |
| The leftovers on the published 5.28.2 | F18, F21, F22 and F26 measured again before their issues were filed, with `probes/ci-run-ids*.sh`, `failed-sends.cs` and `merge-round-trip-*.{cs,py}` pinned to 5.28.2 in a scratch copy | `results/ci-run-ids-5.28.2.txt`, `results/failed-sends-5.28.2.txt`, `results/merge-round-trip-cases-5.28.2.txt`, `results/e2e-r16-5.28.2.txt` |
| The proofs on each published release | R1 on 5.27.14, R3 on 5.28.0, R2 on 5.28.1, from nuget.org, with the probes above | `results/r1-proof-*.txt`, `results/r3-proof-5.28.0.txt`, `results/r2-proof-5.28.1.txt` |

No probe of their own, as they are `kronikol` commands over a file:

- `results/breakfastprovider-kinds.txt`: `query interactions --group-by category,kind` on two real BreakfastProvider reports.
- `results/real-report-merge-diff.txt`: `--group-by category,kind,method` on one of them before and after `kronikol merge`.
- `results/ingest-labels.txt`: `kronikol ingest` of a four-record NDJSON, then the same query.
- `results/red-green-merge-facts.txt`: the patch's first two theories in `MergeReadsEveryTrackersMethodTests`, with the prototype's `ReadMethod` and with 5.27.13's.
- `results/merged-report-readers.txt`: `query history` and `history gate --fail-on behaviour-change` over the merge that said behaviour changed.
- `results/r1-test-runs.txt`: the core and the three database suites on R1.
- `results/r2-test-runs.txt`: the broker suites and the core on R1 and on R2.
- `results/r3-test-runs.txt`: OTLP and the core on R3 and on Q6 (b), red and green, with each patch's sha1.
- `results/r3-on-the-stack.txt`: `r3-span-kinds` on R1, R3, R2 and Q5 (b) together, against R3 alone.
- `results/r3-option-send-is-client.txt`: what Q6 (b) moves, indexing its other `r3-option-send-is-client-*` files, each a named probe on that tree.
- `results/merge-folder-attachments.txt`: F24, `kronikol merge <folder>` on a report folder with and without `attachments/openapi.json`.
- `results/test-runs-all-three.txt`: every test project and the browsers on `27cc0e46` with R1, R3, Q6 (b), R2 and Q5 (b) applied, in that order.
- `results/e2e-r16-control.txt`: F26, `dotnet test --filter` ten times on clean `27cc0e46` and ten on R1, R2 and R3 together.

The real reports are not committed: they are a consumer's data, and they were written by other sessions' runs of BreakfastProvider's lanes.

## The matrix's lists

From `results/kinds-by-tracker.tsv`. The plan's findings refer to these.

**(a) A statement-shaped category logged as `Event`: 34.** BigQuery: Cancel; BigQuery: Create; BigQuery: Delete; BigQuery: Insert; BigQuery: List; BigQuery: Other; BigQuery: Query; BigQuery: Read; BigQuery: Update; Bigtable: CheckAndMutateRow (`CheckAndMutate → orders`); Bigtable: MutateRow (`MutateRow → orders`); Bigtable: MutateRows (`MutateRows (×3) → orders`); Bigtable: Other; Bigtable: ReadModifyWriteRow (`ReadModifyWrite → orders`); Bigtable: ReadRows (`ReadRows ← orders`); Bigtable: SampleRowKeys (`SampleRowKeys ← orders`); Spanner: BatchDml; Spanner: BeginTransaction; Spanner: Commit; Spanner: CreateSession; Spanner: Ddl; Spanner: Delete (`DELETE FROM Orders`); Spanner: DeleteSession; Spanner: Insert (`INSERT INTO Orders`); Spanner: InsertOrUpdate (`InsertOrUpdate Orders`); Spanner: Other; Spanner: PartitionQuery; Spanner: PartitionRead; Spanner: Query (`SELECT FROM Orders`); Spanner: Read (`Read Orders`); Spanner: Replace (`Replace Orders`); Spanner: Rollback; Spanner: StreamingRead (`StreamingRead Orders`); Spanner: Update (`UPDATE Orders`).

**(b) `MessageTable` says `Publishes` or `Receives`, logged as `Default` (F5): 10.** EventBridge: PutEvents (`PutEvents [OrderPlaced]`); EventBridge: PutPartnerEvents; SNS: Publish; SNS: PublishBatch; SQS: ReceiveMessage; SQS: SendMessage; SQS: SendMessageBatch; StorageQueues: PeekMessages (`Peek ← orders`); StorageQueues: ReceiveMessages (`Receive ← orders`); StorageQueues: SendMessage (`Send → orders`).

**(c) `MessageTable` says `Neither`, logged as `Event` (F6): 22.** EventHubs: CreateBatch; EventHubs: GetEventHubProperties; EventHubs: GetPartitionIds; EventHubs: GetPartitionProperties; EventHubs: StartProcessing; EventHubs: StopProcessing; Kafka: AbortTransaction; Kafka: BeginTransaction; Kafka: Commit; Kafka: CommitTransaction; Kafka: Flush; Kafka: InitTransactions; Kafka: SendOffsetsToTransaction; Kafka: Subscribe (`Subscribe orders`); Kafka: Unsubscribe; MassTransit: ConsumeFault (`Consume Fault OrderPlaced`); MassTransit: PublishFault (`Publish Fault OrderPlaced`); MassTransit: SendFault (`Send Fault OrderPlaced`); PubSub: Acknowledge (`Ack`); PubSub: ModifyAckDeadline; PubSub: StartSubscriber; PubSub: StopSubscriber.

(d) A response whose kind differs from its request's (F8): 55 operations, every one an `Event` request and a `Default` response: (a)'s 34,
every Pub/Sub operation (9) and every Event Hubs operation (12).

Span kinds: `Default` is `Client` in 109 rows, and `Event` is `Producer` in 93. 34 rows carry a database name with `Producer`, exactly (a)'s.
