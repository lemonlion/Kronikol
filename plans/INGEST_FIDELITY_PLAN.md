# Ingest fidelity plan: what a hand-built capturer needs from the feed (stage 1d)

**Written:** 2026-09-30, at 4.0.0 (`00abca6`), at the owner's request, after a conversation about a Node service
tested under Jest. **Status: in progress. R1 shipped as 4.1.0 (2026-09-30); R2 to R4 follow** (§12). Placed next in
the roadmap at the owner's word (stage 1d, `ROADMAP.md` D31). The questions in §11 were taken as recommended on
2026-09-30, when the owner asked for the plan to be implemented in full. Evidence labels: **RUN**
(measured here), **READ** (in the source, `file:line`, at `00abca6`), **INFERRED** (reasoned from two facts, stated
by neither), **DOC** (a third party's documentation, not measured). The measurements and their sources are in
[`INGEST_FIDELITY_PLAN.harness/`](INGEST_FIDELITY_PLAN.harness/).

## Where it came from

The owner showed a capture setup for a Fastify + GraphQL service tested in-process under Jest: a Jest environment
that writes each test's start and outcome, Fastify hooks for the inbound GraphQL calls, MSW lifecycle events for the
outbound calls to fakes, and a Firestore client wrapper for the emulator. Every record carries the running test's
id; each worker writes its own NDJSON file; `kronikol ingest` renders the report in CI. None of it touches
production code.

The conversation's conclusion was that such a harness is not limited in its ceiling: the platform plans make every
language a capturer that writes the same NDJSON for the same renderer, which is today's ingest pipeline
(`PLATFORM_FOUNDATIONS_PLAN.md` §0). What limits it is what the feed can carry today and what the harness sends.
Five items came out of that, and the owner asked for all five, planned, and next:

| Item | What | Here |
|---|---|---|
| 1 | `kronikol ingest --headers shown\|hidden` (roadmap 1c.3) | **Shipped as 3.36.0** (`ed81a13`, 2026-09-30) while this conversation was under way, the first half of #111. Nothing left to plan |
| 2 | A failed call's `error` on interaction records | **S2** |
| 3 | A retry `attempt` and a source location on tests records | **S3** |
| 4 | `--separate-setup` on `kronikol ingest` | **S4** |
| 5 | A span stream into internal flow, once OpenTelemetry is proven under Jest | **S0** (the proof, done) and **S5** |

The slice numbers follow the items. The release order is by the roadmap's rule 1 and differs (§5).

---

## 0. Summary

Four research passes over the code and a harness of five probes and one Node spike found that the five items are
right and that three of them hide a defect that is live today:

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **`--phase-from-steps` has never drawn the Setup partition it documents.** It sets each call's `phase` field and nothing else; the partition is drawn only at a `Phase` marker (`IsActionStart`), which ingest creates only from a raw `kind: marker` record. Measured: `SeparateSetup = true` with `PhaseFromSteps = true` tags the calls Setup, Setup, Action, Action and draws no `partition`; the same input plus one `Phase` marker line draws it. The claim is in the option's doc comment, `IngestAttribution`'s, the CLI's usage line, the wiki (twice) and the 3.0.45 changelog, where it shipped (`f46ebdb6`, 2026-08-22) with no test of a partition | RUN (P1), READ | §2.3 |
| F2 | **A retried test becomes one passed scenario that still carries the failed attempt's error**, with both attempts' steps and both attempts' calls in one diagram, and `Failures.md` saying `# No failures`. A producer that splits attempts into two test ids gets the opposite: two scenarios, one failed, and a run that reads as failed though the retry passed | RUN (P3) | §2.2 |
| F3 | **OpenTelemetry's documented setup gets almost nothing under Jest, and a harness-only setup gets everything.** `registerInstrumentations()` produced 1 span; the same instrumentations applied the way Jest needs produced 16 (every GraphQL resolver, the Fastify request, the outbound fetch), each carrying its test's id, and 31 in 3 traces for two concurrent tests with no trace holding two tests. No test file changed | RUN (S0: V1 to V4) | §2.5 |
| F4 | **Kronikol's OTLP reader already reads what the Node SDK writes.** The spike's spans, serialised by `@opentelemetry/otlp-transformer`, came through `OtlpTraceReader.ReadJson` whole: 16 spans, the parent tree, kinds, scope names, durations and the test-id attribute. Harness and span clocks agree to 0 to 1 ms, inside internal flow's 50 ms window | RUN (P4, S0) | §2.4 |
| F5 | **Internal flow is typed on `System.Diagnostics.Activity`**, a process-wide store of them, and reads seven members. An `Activity` cannot be built faithfully from a foreign span (its `SpanId` cannot be set), and the default span filter keeps only traces holding a .NET source, which would drop every Node span | READ | §2.4 |
| F6 | **A failed call's status already survives ingest; only its text is lost.** A response with `statusCode: "!TypeError"` draws `!TypeError`, is ranked as an error in `Failures.md` and fingerprints as a failure. The message chain (`error`) is ignored, so the data files, `kronikol query http` and anything reading `httpInteractions[].error` never see why the call failed. `INGEST_FEED_PLAN.md` §3.2 overstates what the field feeds: the diagram and the fingerprint read the status, not `error` | RUN (P2), READ | §2.1 |
| F7 | **An `error` that is not a string is ignored today and would become a malformed line.** A capturer that put an object in `error` gets its line read and the object dropped; a strictly typed `string?` member would reject the whole line | RUN (P2), READ | §3.2 |
| F8 | **An ingested failed step shows no message in `Failures.md`.** The digest prints a failing step's `FailureMessage`; both ingest lanes put a step's error into `Comments` instead | READ | §2.2 |
| F9 | **The report model already has every field items 2 and 3 need.** `Scenario.Attempt`, `SourceFile` and `SourceLine`, `Feature.SourceFile`, `ScenarioStep.SourceFile`, `SourceLine` and `FailureMessage`, `httpInteractions[].error`: every writer, the schema, CTRF, history and `kronikol query` carry them. The gap is input only | READ | §2.1, §2.2 |
| F10 | **The Cucumber lane has attempts and scenario locations but drops step locations and the step phase.** It parses each Gherkin step's location and never maps it (#76's remainder, on the ingest lane), and its step markers carry no `keywordType`, so a non-English keyword gets no phase | READ | §3.3, §3.4 |
| F11 | **`kronikol merge` drops `error`, `attributionSource` and `expiredFrom`, and `kronikol export` sends a failed send as a span without an error status**, because `!HttpRequestException` is not in the exporter's list of failure words | READ | §3.2 |
| F12 | Twelve more defects outside this plan's slices, in the same code paths, listed in §8 and placed as roadmap row 1.15 | READ, INFERRED | §8 |

Five slices, four releases:

| Slice | What | Bump |
|---|---|---|
| **S0** | The spike: OpenTelemetry under Jest, and the reader over its output. **Done** (§2.5), recorded in the harness | none |
| **S2** | `error` on interaction records, read leniently; a failure half's status defaulted; `merge` keeps `error`, `attributionSource` and `expiredFrom`; `export` marks a `!Type` status as an error; `Failures.md` prints a failed call's error | minor |
| **S3** | `attempt`, `sourceFile` and `sourceLine` on tests records; a second `start` read as a retry; the winner-only scenario the Cucumber lane already builds; a step's error as its `FailureMessage`; the Cucumber lane's step locations | minor |
| **S4** | `--separate-setup`; the Setup/Action boundary synthesised from the steps or the calls' phases when `SeparateSetup` is on; the false documentation corrected; the Cucumber lane's step `keywordType` | minor |
| **S5** | `--spans`: OTLP/JSON lines into internal flow, through a neutral span record; the Jest recipe on the wiki | minor |

### What the owner's report gains

| In the report | The harness as drawn | Plus the harness changes (no Kronikol release) | Plus stage 1d | Still out of reach |
|---|---|---|---|---|
| Every boundary call, with bodies | yes | yes | yes | calls outside the three hooks |
| A call that failed | nothing, or the `!Type` status if the harness writes one | the status | the status and the message chain, in the data files, `query http` and `Failures.md` (S2) | |
| Step bars; the failing step's calls in `Failures.md` | no | yes, from lifecycle steps or a `step()` helper | plus each step's source line and failure message (S3) | named steps need a line in the test |
| The Setup/Action partition | no | only through a C# ingest with a hand-written `Phase` marker | `--separate-setup`, the boundary from steps or phases (S4) | |
| Assertion notes | no | yes, from an `expect` wrapper | plus the assertion's source line (S3) | the value of each sub-expression |
| A retried test | one passed scenario carrying the first attempt's error | the same | the winning attempt, `attempt`, a `retry N` label, flaky in history (S3) | the earlier attempts' calls (§11 Q5) |
| Where the test is written (`written at file:line`, CTRF `filePath`) | no | no | yes (S3) | `Thrown at` from a JavaScript stack (foundations L6) |
| Internal-flow popups and the whole-test flow | no | no | yes, with the S0 setup and `--spans` (S5) | a service that emits no spans |
| Parameterised groups from `test.each` | no | no | no | the row values are not in Jest's events |

---

## 1. How far each claim was checked

- **S0, the spike** (`INGEST_FIDELITY_PLAN.harness/otel-jest/`, `./run.sh`): a Fastify 5 and Mercurius 16 service
  standing in for the owner's, run in-process under Jest 30.5.2 on Node 22.22.2, four variants, results in
  `results/`. Run three times in a scratch copy and once more from the harness folder; the span counts were the
  same each time.
- **P1 to P3** (`IngestFidelityProbeTests.cs`): a temporary xUnit class compiled into
  `tests/Kronikol.Tests/Ingestion/`, run alone with the .NET 10.0.401 SDK and deleted. It feeds
  `IngestPipeline.Run` raw NDJSON lines as an external capturer writes them. **P4**
  (`IngestFidelitySpanProbeTests.cs`) ran the same way in `tests/Kronikol.Tests.Otlp/`. Output:
  `probe-results.txt`.
- **READ** claims come from four passes over the code at `00abca6`, one per item (their full notes, with the tests that
  will flip and the insertion points, are the harness's `research-notes.md`), and were spot-checked where this
  plan leans on them (the exporter's failure words, the merge reader, the component diagram's error count, one SDK
  handler, the TCP tap's connect path, the implicit action start, the Cucumber retry labels, the digest's
  `written at` line).
- **Not measured**, and said so where it matters: other test runners (Vitest, `node:test`), other GraphQL servers
  (Apollo), Node 24, a loaded CI machine's clocks, OTLP/JSON from SDKs other than JavaScript's (§9).

---

## 2. The feed today, for a hand-built capturer

### 2.1 A call that failed (item 2)

In-process, a call that throws is logged as two halves: the request, then a response half whose status is the
exception's type behind a bang and whose `Error` is the message chain (`TestTrackingMessageHandler.cs:271-294`;
`FailedSend.Status` `"!" + type` and `FailedSend.Describe`, `FailedSend.cs:16-30`; 3.18.0). Only that handler and the
Cosmos handler set `Error` (`CosmosTrackingMessageHandler.cs:131`). `RequestResponseLog.Error` is documented as null
on a request (`RequestResponseLog.cs:62-68`).

**Most of a failure's visibility comes from the status, which the feed already carries** (READ): the diagram draws
`!Type` verbatim (`PlantUmlCreator.cs:1156-1170`), the fingerprint takes the response's status
(`InteractionShape.cs:182-207`), `Failures.md`'s status column and error ranking read it
(`FailuresDigestGenerator.cs:309-317`, `:349-362`; `InteractionStatus.cs:101-112`), and so do `flow --errors-only`
and `services`. **`Error` itself is read only by** the data files (`ReportGenerator.cs:4806` JSON, `:5055` XML,
`:5434-5435` YAML; schema `:6444`) and `kronikol query http` (`QueryCommand.Payloads.cs:296-297`).
`IngestRoundTripTests.cs:125` already proves the diagram does not use it: the diagrams are byte-identical while
`error` differs.

**The wire drops it.** `InteractionRecord` has 32 members and no `error` (`InteractionRecord.cs:37-192`);
`FromLog` (`:208-245`) and `ToLog` (`:252-303`) never copy it; `RequestResponseLogRoundTripTests.cs:70` pins it as a
known gap "for 14.1", and `IngestRoundTripTests.cs:102-105` pins it as the one allowed difference.

**P2 (RUN)**, an external capturer's failure half with `statusCode: "!TypeError"` and an `error` string:

```
P2 Response http://localhost:5000/charges: statusCode=null statusText="!TypeError" error=null
  diagram draws '!TypeError': True
  Failures.md names '!TypeError': True; names 'ECONNREFUSED': False
```

The same run's second response had `error: {"code":"ECONNRESET"}` and no status: read, object dropped, nothing
marking it failed, no diagnostic. Today's reader ignores unknown members (`InteractionRecord.cs:23-24`, web
defaults at `:29-34`), so a capturer may have put anything in `error`.

Where the text travels once it is on the wire, and where it stops (READ): `MergeableReportReader.ReadInteractions`
(`:473-527`) reads no `error`, `attributionSource` or `expiredFrom`, so `kronikol merge` writes nulls;
`OtlpSpanMapper.StatusOf` (`:312-330`) marks a status an error only for `error`, `failed`, `failure`, `fault`,
`timeout` or `exception` (`:332-333`), so `kronikol export` and the live export sink send a failed send as an
`Unset` span with no message.

### 2.2 A retried test, and where a test lives (item 3)

**The model has it all** (READ): `Scenario.Attempt`, 1-based, "Null where the runner reports nothing about
attempts, which is every lane but Cucumber Messages today" (`Scenario.cs:58-65`); `Scenario.SourceFile`, "a
project-relative path with forward slashes" (`:91-98`); `Scenario.SourceLine`, the `Scenario:` line (`:100-105`);
`Feature.SourceFile` (`Feature.cs:14-21`); `ScenarioStep.SourceFile`, "File name only, not a full path"
(`ScenarioStep.cs:28`), `SourceLine` (`:30-31`) and `FailureMessage` (`:20-25`).

**Everything downstream already uses them** (READ): the digest's header says `written at file:line`
(`FailuresDigestGenerator.cs:661-662`) and a failing step prints its own `(file:line)` and `FailureMessage`
(`:710-717`); `kronikol query failures` prints `at source:line`; CTRF writes `retries = Attempt - 1`, `filePath` and
`line` (`CtrfReportGenerator.cs:178`, `:193-194`); history turns a pass on attempt 2 or later into Flaky, "passed on
retry N in this run" (`HistoryAnalyzer.cs:379-395`); `kronikol query scenarios` prints `attempt N`.

**The tests records carry none of it.** `TestRunRecord` has 32 members; none is an attempt, a file or a line
(`TestRunRecord.cs:33-154`), and `path` and `step` are taken by attachments. `FeatureSynthesizer` builds one
accumulator per test id (`:47-60`) over the records sorted by time (`:62`): the first `start` wins the start time
(`:79`), both starts' tags are appended (`:88`), both attempts' steps land in one list, and each `end` overwrites
the status while keeping an earlier `error` when the later end has none (`:106-108`). The scenario gets
`Id = testId` and no attempt or location (`:151-177`). A step's error goes to `Comments`, not `FailureMessage`
(`:386`, `:401`), which is why an ingested failing step shows no message in `Failures.md` (F8).

**P3 (RUN)**, one test id, a failed attempt then a passing one:

```
P3 retried test: scenarios with id=1
  durationSeconds=2 attempt=null sourceFile=null sourceLine=null ...
  result=Passed steps=[Attempt one | Attempt two] httpInteractions=4 error=expected SETTLED, received PENDING
  Failures.md first line: # No failures
P3 split ids: scenarios=2 results=[Charges a card:Failed,Charges a card:Passed]
  Failures.md first line: # Failures — 1 of 2 scenarios
```

**The Cucumber lane already does retries right** (READ), and S3 copies it rather than inventing a second rule:
attempts are grouped per test case (`CucumberFeatureSynthesizer.cs:161-175`), the last is the winner
(`:202-203`), only the winner contributes steps, markers and attachments (`:274`, `:278`, `:334-345`), each earlier
attempt adds a `retry N` label (`:490-492`, "Earlier attempts are not shown as scenarios; a label keeps the
flakiness visible"), and the scenario gets `Attempt = winner.Attempt + 1` (`:524`), `SourceFile` and `SourceLine`
from the Gherkin node (`:527-528`). It parses each step's location and never maps it (the step built at `:417-432`
sets none; F10), and it puts a step's failure in `Comments` (`:389`, `:424`).

A per-test attempt is not a CI attempt. `GITHUB_RUN_ATTEMPT` becomes the history run id `gh:<run>:<attempt>`
(`CiMetadata.cs:52-54`); a same-run re-execution in a new process is `EVIDENCE_SURVIVES_A_RERUN_PLAN.md`'s case
(`KeepRuns`, the ledger overlay). `attempt` here is the runner's retry inside one run, what Jest's `retryTimes`
does, and it feeds the ledger's per-position attempt column (`HistoryRunBuilder.cs:83`).

### 2.3 The Setup partition (item 4)

`kronikol ingest` has no `--separate-setup` (the flag parse is `IngestCommand.cs:57-247`; `SeparateSetup`,
`HighlightSetup` and `SetupHighlightColor` appear nowhere in the tool), so the partition is unreachable from the
command line; `INGEST_FEED_PLAN.md` Q11 recorded this and the S3 run measured it (3 of 6 LightBDD diagrams).

**The partition is drawn only at a marker** (READ): `hasActionStart = separateSetup && tracesForTest.Any(t =>
t.IsActionStart)` (`PlantUmlCreator.cs:193`), with no marker every partition branch is skipped
(`PlantUmlCreatorTests.cs:1848-1859` pins it), and `PlantUmlCreator` never reads a call's `Phase`. In-process the
marker comes from `StartAction()` (`TrackingDiagramOverride.cs:91-112`) or from the handler's implicit rule: the
first request made during a non-Given step after a request made during a Given, And or But step, logged just before
that request (`TestTrackingMessageHandler.cs:341-368`; `TestTrackingMessageHandlerTests.cs:1398`: no marker when the
first step is a When). Ingest creates one only from a raw `kind: marker`, `markerKind: Phase` record
(`InteractionRecord.cs:327-330`).

`--phase-from-steps` builds step windows (`IngestAttribution.cs:440-462`) and sets each unphased call's `phase`
(`:471-505`). After that, `Phase` is read only by the data files, the merge reader and `kronikol query`
(`ReportGenerator.cs:4803`, `:5063`, `:5447`; `QueryCommand.GroupBy.cs:103`). **P1 (RUN):**

```
P1 p1-separate-only:                 SeparateSetup=True  PhaseFromSteps=False -> partition=False
P1 p1-separate-and-phase-from-steps: SeparateSetup=True  PhaseFromSteps=True  -> partition=False phases=[Setup,Setup,Action,Action]
P1 p1-separate-and-phase-marker:     SeparateSetup=True  PhaseFromSteps=False -> partition=True
```

The false claim (F1) is at `IngestPipeline.cs:188-193`, `IngestAttribution.cs:464-470`, `IngestCommand.cs:564-565`,
the wiki's `Ingesting-External-Captures.md:225` and `:377-383`, `CHANGELOG.md:5000` (3.0.45),
`PLATFORM_FOUNDATIONS_PLAN.md` §12.4 and `JAVA_PORT_PLAN.md` Appendix C. The only pipeline test with
`PhaseFromSteps` asserts the phase on the data file and never looks at a diagram
(`IngestAttributionTests.cs:254-297`).

The Cucumber lane's step markers set `Keyword` but no `KeywordType` (`StepMarkerRecord`,
`CucumberFeatureSynthesizer.cs:552-567`); the resolved Gherkin type sits on `CucumberStepWindow.KeywordType`, whose
doc says "This is what phase attribution reads", and nothing reads it. So `Angenommen` and `Wenn` get no phase.

### 2.4 Internal flow (item 5)

`kronikol ingest` turns internal flow off, "there are no in-process spans to show" (`IngestPipeline.cs:282-289`),
and the contract has no span stream (`PLATFORM_FOUNDATIONS_PLAN.md` G4 and F5; roadmap 14.5: "a span stream
designed after 1c.4", which shipped as 3.35.1).

**What internal flow is made of** (READ): `InternalFlowSpanStore` is a process-wide static queue of
`System.Diagnostics.Activity` (`InternalFlowSpanStore.cs:14-43`), fed by an `ActivityListener` and the
OpenTelemetry exporter extension. The pipeline reads seven members: `TraceId`, `SpanId`, `ParentSpanId`,
`DisplayName ?? OperationName`, `Source.Name`, `StartTimeUtc` and `Duration`; never kind, status, tags or events.
Segments are public and typed on it: `InternalFlowSegment(..., Activity[] Spans)` (`InternalFlowSegment.cs:10-16`).
Spans reach calls only through the calls (`InternalFlowSegmentBuilder.cs:102-255`, the 3.35.1 rules): a call with
an `ActivityTraceId` (lowercase hex, compared ordinally) takes that trace's spans, narrowed to its `ActivitySpanId`'s
subtree when other tests' calls share the trace, and a span must start inside the call's window, from 50 ms before
the request to the response (`:186-216`).

**The two facts that shape S5** (READ, INFERRED): an `Activity` cannot be built from an OTLP span faithfully (its
`SpanId` is generated and cannot be set, its `Source` needs a listened `ActivitySource`, and starting one is
visible to every listener in the process), so ingested spans need a neutral type; and the default granularity,
`AutoInstrumentation`, keeps only traces holding one of 14 .NET source names (`InternalFlowSpanCollector.cs:11-27`,
`:54-71`), which drops every Node span.

**What already exists for the rest** (READ, RUN): `InteractionRecord` carries `activityTraceId` and
`activitySpanId` and `ToLog` copies them (`:177-181`, `:297-298`), so the interaction stream needs no new member;
`OtlpTraceReader.ReadJson` (`Kronikol.Extensions.Otlp`, `:59-108`) decodes one OTLP/JSON document into `OtlpSpan`,
which has every field internal flow needs, and P4 read the Node spike's file with it:

```
P4 spans read: 16 from v2-spans.otlp.jsonl
  eb7b7561 50d3989e0b177afe parent=- kind=Server scope=@fastify/otel name="request" ms=85.354 test=T-V2
  eb7b7561 8b7f9746a6a508c9 parent=61125bf9491f6609 kind=Internal scope=@opentelemetry/instrumentation-graphql name="graphql.resolve charge" ms=63.29 test=T-V2
  eb7b7561 9cfbdd5902302f82 parent=8b7f9746a6a508c9 kind=Client scope=@opentelemetry/instrumentation-undici name="GET" ms=15.333 test=T-V2
  ...
P4 the tail mapper would draw 1 arrow(s) from them: GET http://127.0.0.1:45053/score/c2
```

The one arrow the tail mapper (`SpanToInteractionMapper`) would draw is the call MSW passed through, which the
harness already captures: spans are for internal flow here, not for arrows (§11 Q2).

### 2.5 OpenTelemetry under Jest (S0, done)

The gate the owner set for item 5. The harness README has the detail; the results:

| Variant | Setup | Spans |
|---|---|---|
| V1 | `registerInstrumentations()` with the http, undici, GraphQL and Fastify instrumentations, then load the service: the documented setup | **1**, the undici span of the passed-through call. OpenTelemetry's auto-instrumentation hooks Node's `require`; Jest loads modules through its own registry, so GraphQL is never patched, and nobody registered `@fastify/otel`'s plugin. Undici works because its hooks are diagnostics channels, which are process-wide |
| V2 | `@fastify/otel` with `registerOnInitialization: true` (Fastify announces each instance on a diagnostics channel), the GraphQL instrumentation's patches applied through Jest's own `require` via `getModuleDefinitions()`, undici | **16**: 11 GraphQL (parse, validate, the operation, every field resolver), 4 Fastify, 1 undici. The harness's Fastify hook sees the request's span; the MSW listener runs inside `graphql.resolve charge`, same trace; every span of the request carries the test's id |
| V3 | V2, two tests' requests at once | **31** in 3 traces, **no trace holding two tests**; each MSW event saw its own test's trace and id |
| V4 | V2's setup in Jest `setupFiles` and `setupFilesAfterEnv`; the test file has no tracing code | **16** |

Found on the way (RUN): the patches must land before the service loads (Mercurius destructures `graphql`'s
functions when required, so `setupFiles` is the right moment); `registerOnInitialization` leaks across test files in
one worker (`FastifyError: The decorator 'opentelemetry' has already been added!`) unless the harness calls
`disable()` in `afterAll`; an MSW-mocked call has no client span (MSW answers before undici), but its listener runs
inside the resolver's span, so the harness can stamp the call's `activityTraceId` and `activitySpanId` from
`trace.getActiveSpan()`; the harness's wall clock and the spans' start times agreed to 0 to 1 ms over 15
observations. Three things this service needed to run under Jest on Node 22 at all, unrelated to OpenTelemetry,
are in the README (MSW pinned to 2.11, an ESM-only dependency overridden, `--experimental-vm-modules`).

---

## 3. Design

Every slice changes `src/Kronikol/Ingestion/` (roadmap track D) and the tool's `IngestCommand.cs`, and S5 also
changes `src/Kronikol/InternalFlow/` and `ReportGenerator.cs` (track B). The rule for a new ingest flag is not
`VerbTable`'s, which covers `kronikol query` only (`VerbTable.cs:31-35`; `INGEST_FEED_PLAN.md` Q11 borrowed it):
it is the switch case, the mapping onto the options or the request, `PrintUsage`, the wiki's flag table, one
`IngestCommandTests` fact, as `--headers` did (`ed81a13`).

### 3.1 S1: `--headers` (item 1)

Shipped as 3.36.0 (`IngestCommand.cs:147-155`, `:312-313`; `IngestCommandTests.cs:137-186`). #111 asked for it and
for the rest of the report's toggle defaults "without a flag for each setting"; that second half is the options
file, `PLATFORM_FOUNDATIONS_PLAN.md` F4, and is not this plan's (§10). #111 is still open.

### 3.2 S2: a failed call's `error` (item 2)

**The member.** `InteractionRecord` gains `[JsonPropertyName("error")] public string? Error { get; init; }`, the
name the tests records and the report already use (`TestRunRecord.cs:54`, `httpInteractions[].error`), with a
converter that reads any JSON value: a string as it is, anything else as its raw JSON text (F7). `FromLog` writes
`Error = log.Error`; `ToLog` restores it (`:294-302`). Null is omitted on the wire (`WhenWritingNull`, `:29-34`).

**Two rules for records the in-process path never writes** (§11 Q8):

- A `Response` line with `error` and no `statusCode` gets the status `!Error`, so every status-reading surface
  (the diagram, the fingerprint, the digest's ranking, `--errors-only`) treats it as the failure it says it is. Only
  the `!` prefix escapes the status label's title casing (`PlantUmlCreator.cs:1159`).
- A `Request` line with `error` keeps it on the request log (the data files show it) and counts in one diagnostic
  that says to write it on the response half. No response half is synthesised.

**Where the text then travels**, fixed in the same slice because each is the record's own path (F11):
`MergeableReportReader.ReadInteractions` reads `error`, `attributionSource` and `expiredFrom` back, so `kronikol
merge` stops writing nulls for the three; `OtlpSpanMapper.StatusOf` treats a `!`-prefixed status as
`OtlpStatusCode.Error` with the log's `Error` (else the status) as the message, for `kronikol export` and the live
sink.

**`Failures.md`** (§11 Q9): under the calls table of a failed scenario, each call whose response carries an error
gets one line, `` `<address>` <status>: <error, one line, capped at 400 characters as `query http` caps it> ``, so the
digest says why a call failed where it already says that it did. This changes the digest for in-process runs too,
where `Error` exists since 3.18.0 and was never shown there.

**What does not change:** the diagram (it reads the status), the fingerprint, `Pair(...)`'s signature (an optional
parameter would break binary compatibility; `response with { Error = … }` does it).

### 3.3 S3: attempts and source locations (item 3)

**The members**, all optional, all on the tests records:

| Member | On | Becomes |
|---|---|---|
| `attempt` | `start` (and `end`, checked against it) | `Scenario.Attempt`, 1-based: 1 for the first run, 2 for the first retry, as the model and the `retry N` label count |
| `sourceFile` | `start` | `Scenario.SourceFile`, and the feature's `SourceFile` when it has none yet (the first one seen wins, as on the ReqNRoll lane) |
| `sourceFile` | `step`, `assertion` | `ScenarioStep.SourceFile`, reduced to the file name, the step contract (`ScenarioStep.cs:28`) |
| `sourceLine` | `start`, `step`, `assertion` | `Scenario.SourceLine` (the test's declaration), `ScenarioStep.SourceLine` (the call site) |

**Paths** (§11 Q6): backslashes become `/` and a leading `./` goes; an absolute path under the source root becomes
relative to it. The root is `--source-root <dir>` (`IngestRequest.SourceRoot`), by default the directory the tool
runs in, which in CI is the checkout. An absolute path outside it is kept and counted in one diagnostic, because the
3.1.0 rule is that a build machine's paths do not belong in a downloadable artifact (`CHANGELOG.md:4469`) and the
producer should know it wrote some.

**Attempts** (§11 Q5). One test id per test, as Jest's retries keep the same test; each `start` opens an attempt.
A `start` without `attempt` after another `start` of the same id is read as the next attempt, which fixes F2 for the
producers that exist today, with one diagnostic line naming how many tests were read that way. A pre-pass in the
pipeline splits each test's records by its starts' timestamps; then, as on the Cucumber lane:

- the scenario is built from the **last attempt**: its status, error, duration, steps, assertions and attachments,
  and `Attempt` = its number;
- each earlier attempt adds the label `retry N` (the Cucumber lane's wording, `:490-492`) and nothing else;
- the calls whose timestamps fall in an earlier attempt's window (pairs judged by their request) are left out of
  the scenario before replay, and one diagnostic counts them;
- tags are no longer duplicated (only the winner's `start` is read).

A last attempt with no `end` stays the "no verdict" case it is today (`ResultWhenUnknown`). Two test ids for one
test remain two scenarios; the wiki says which form to write.

**The step's failure** (F8): a `step` or `assertion` record's `error` also becomes `ScenarioStep.FailureMessage`,
so `Failures.md` prints it under the failing step; `Comments` keeps it too, because the HTML renders `Comments`
(`ReportGenerator.cs:3694-3698`) and would otherwise change.

**The Cucumber lane** (F10), the same fields: each step's `ScenarioStep.SourceFile` (the feature's file name) and
`SourceLine` from its parsed location, and its failure as `FailureMessage`. That is #76's "Gherkin steps carry no
location" on the ingest lane; its in-process ReqNRoll half stays open.

**What else moves:** the schema's `Feature.sourceFile` description says it is null for "the tests NDJSON"
(`ReportGenerator.cs:6145`), which stops being true; the `ScenarioStep.SourceLine` doc says "zero when unknown"
where the code stores null (`StepCollector.cs:231`), corrected with it.

### 3.4 S4: `--separate-setup` (item 4)

**The flag.** A plain switch, `--separate-setup`, setting `SeparateSetup = true`, after `--phase-from-steps` in the
usage (the `--diagnostics-section` precedent). `HighlightSetup` stays on and its colour stays the default; the rest
of the report's options are the options file's (#111's second half, F4).

**The boundary** (§11 Q7). When `SeparateSetup` is on, the pipeline gives each test one synthesised `Phase` marker,
the record `InteractionRecord.FromLog` already writes for `StartAction`, unless:

- the capture already carries a raw `Phase` marker for the test (the capture wins, as step bars do,
  `IngestPipeline.cs:721-727`), or
- the capture draws the test's step bars itself (a projected in-process store: its run drew no boundary, so none is
  added and "a store projected through the writer ingests to the report the in-process run wrote" stays true).

It goes at the **start of the first Action step that follows a Setup step** (`BuildStepWindows`,
`PhaseForStep`), else, when the steps say nothing, **just before the first call phased Action that follows a call
phased Setup** (phases a capturer wrote, or `--phase-from-steps` set). That mirrors the in-process rule: a boundary
only after something happened in setup, placed before the first action call. It is inserted at the front of the
records so it sorts before a call at the same instant (`ByTimestamp` is stable, `:747-753`). One per test: the
drawing uses only the first (`PlantUmlCreator.cs:194`).

Gated on `SeparateSetup` because a marker also breaks a run of identical calls for the collapser, so every ingest
without the option stays byte-identical. With it, a library caller who already set both options gets the partition
the documentation promised: a bug fix that changes output, called out in the changelog.

**Corrections** (F1): the doc comments, the usage line (`--phase-from-steps` "tags each call with the phase of the
step it happened during; with `--separate-setup` the diagram draws the Setup partition"), the two wiki passages,
`Report-Configuration.md:123` ("before the first non-GIVEN BDD step" holds only when a Given-step call went through
the test client's handler), and the two plans that copied the claim. The 3.0.45 changelog entry stays as written;
the release's entry says it was false.

**The Cucumber lane** (F10): `StepMarkerRecord` passes the pickle step's type as `KeywordType`, so phases and the
boundary work for any Gherkin language.

### 3.5 S5: the span stream (item 5)

**The wire** (§11 Q1): **OTLP/JSON lines**, one OTLP `TracesData` object (`{"resourceSpans":[...]}`) per line, as
`@opentelemetry/otlp-transformer`'s `JsonTraceSerializer.serializeRequest` writes it (S0) and as the OpenTelemetry
Collector's file exporter writes JSON (DOC). Ids hex, times in nanoseconds as a string or a number, kinds as numbers
or names: what `OtlpTraceReader` already accepts (`:143-164`, `:240-255`, `:272-292`). The foundations settled the
same (`PLATFORM_FOUNDATIONS_PLAN.md` §11, Decision 5: "Per-platform residue: write the SDK's spans to an OTLP-JSON
file"), and so did Go (`GO_PLATFORM_PLAN.md` §3.5). It is the "third stream" F5 asked for; 14.5 freezes it.

**The call side needs nothing new.** A call joins its spans through `activityTraceId` (the trace the service's
spans carry) and `activitySpanId` (the span the call was made under, or the one the server span names as its
parent). Ingest lowercases both and reads an empty one as absent (`ToLog` copies them verbatim today). The S0
harness stamps them from `trace.getActiveSpan()` in its Fastify hook and its MSW listener.

**A neutral span** (F5): `Kronikol.InternalFlow.FlowSpan`, a public record of the seven fields internal flow reads
(`TraceId`, `SpanId`, `ParentSpanId`, `Name`, `Source`, `StartTimeUtc`, `Duration`) plus `Service`. The builder,
the renderer, the component-flow builder and the diagnostics work on it; an `Activity` becomes one at collection.
The public `Activity`-typed members stay (the major is reserved): `InternalFlowSegment` gains `FlowSpans`, filled on
every path, and `Spans` keeps its `Activity`s in-process and is empty for ingested spans. In-process output must
not move a byte (T-rows in §4 pin it). `OtlpSpan.ToFlowSpan()` in the OTLP package makes one from a parsed span,
with the scope name as its source (the swimlane), else the service.

**The pipeline.** `IngestRequest.Spans` takes `FlowSpan`s in memory (the foundations' L9: in-process .NET will feed
the pipeline records, not files); when any are given, the pipeline hands them to the generator in place of the
static store's, and the store is neither read nor written, so an ingest in a process that captured spans of its
own neither shows them nor loses them. Spans are de-duplicated by trace and span id. The span filter does not apply
its `.NET`-only `AutoInstrumentation` list to supplied spans: they are `Full` unless `Manual` names sources
(§11 Q3), since the capturer chose what to export. Diagnostics: spans read, span lines malformed, calls whose
`activityTraceId` matches no span (the likely harness mistake), spans no call took; and the store's "0 spans"
warning counts the spans the report used (`ReportDiagnostics.cs:61-72`).

**The command.** `--spans <file|dir|glob>`, repeatable, resolved like the inputs; the tool reads each line with
`OtlpTraceReader.ReadJson` (the tool already references the OTLP package, `Kronikol.Tool.csproj:44`; the reader stays
there, §11 Q4), with the malformed-line and `--strict` rules the interaction reader has. `--spans` turns internal
flow on. Span files are taken out of the interaction inputs, as `--tests` and `--cucumber-messages` files are
(`IngestCommand.cs:257-280`), and an interaction input whose first line holds `resourceSpans` is skipped with one
line saying to pass it with `--spans`, instead of a malformed line per span.

**What turning internal flow on changes for that report** (READ): each request arrow's label is wrapped in its
`[[#iflow-…]]` link, the popup, flame chart and toggle scripts are included, and whole-test flows appear
(`ReportGenerator.cs:331-334`, `:1369-1375`; `PlantUmlCreator.cs:368-377`). Only when spans are given, so every
existing ingest stays byte-identical.

**The recipe.** A wiki page, `Ingesting-From-Jest`, written from S0's harness: `setupFiles` with the provider, the
span processor that stamps `kronikol.test.id`, `registerOnInitialization` and its `disable()`, the GraphQL patches
through Jest's `require`, undici; stamping `activityTraceId` and `activitySpanId` in the capture points; one
`JsonTraceSerializer` line per test file in `afterAll`; the `kronikol ingest ... --spans` line. The owner's
steps from the conversation (lifecycle steps, an `expect` wrapper, the test id in `AsyncLocalStorage` bound in the
Fastify `onRequest` hook) go on the same page, since they are the rest of the same harness.

---

## 4. Tests, in the order they are written

Every fact is red first. Unit and pipeline facts in `tests/Kronikol.Tests` (the OTLP ones in
`tests/Kronikol.Tests.Otlp`), Playwright facts in `tests/Kronikol.Tests.EndToEnd` for what a reader sees (the
partition, the popups). Pipeline facts share the `DiagramsFetcher` collection, scope every store read by their own
test id and never clear the store themselves (`INGEST_FEED_PLAN.md` §5's cautions hold).

| # | Fact | File | Red on | Slice |
|---|---|---|---|---|
| T1 | `--separate-setup` is in the usage and reaches the report: the P1 input through `IngestCommand.Run` draws `partition #F6F6F6 Setup` with the flag and none without | `Tool/IngestCommandTests.cs` | unknown option | S4 |
| T2 | The boundary from steps: P1's Given then When, `SeparateSetup` on, draws the partition around the Given call only; with `SeparateSetup` off the diagram source is byte-identical to today's | `Ingestion/IngestPipelineTests.cs` | F1 (P1 measured) | S4 |
| T3 | The boundary from phases: no steps, calls phased Setup then Action by the capturer, the partition wraps the Setup calls; a first step that is a When draws none (the in-process rule, `TestTrackingMessageHandlerTests.cs:1398`) | `Ingestion/IngestPipelineTests.cs` | no marker | S4 |
| T4 | The capture wins: a raw `Phase` marker yields exactly one boundary where the capture put it; a projected store (`IngestRoundTripTests`) stays byte-identical with and without `SeparateSetup` | `Ingestion/IngestPipelineTests.cs`, `IngestRoundTripTests.cs` | a second boundary | S4 |
| T5 | Cucumber: a German feature's `Angenommen` and `Wenn` steps phase as Setup and Action | `Ingestion/Cucumber/CucumberIngestPipelineTests.cs` | no `KeywordType` | S4 |
| T6 | Playwright: an ingested report with `--separate-setup` renders the Setup group in the diagram's SVG | `Kronikol.Tests.EndToEnd` | no partition | S4 |
| T7 | Attempts: P3's input with `attempt` 1 and 2 gives one scenario, `Passed`, `attempt: 2`, `errorMessage` null, the second attempt's steps only, a `retry 1` label, the second attempt's two calls only; without `attempt` the same, plus the diagnostic | `Ingestion/FeatureSynthesizerTests.cs`, `IngestPipelineTests.cs` | F2 (P3 measured) | S3 |
| T8 | History and CTRF see it: the run above folds as Flaky "passed on retry 2 in this run"; CTRF writes `retries: 1` | `History/HistoryAnalyzerTests.cs`, `Reports/CtrfReportGeneratorTests.cs` | `Attempt` null | S3 |
| T9 | Locations: `sourceFile`/`sourceLine` on a `start` reach the scenario, the feature, `Failures.md`'s `written at` and CTRF's `filePath`; on a step, its `(file:line)`; an absolute path under `--source-root` becomes relative, one outside is kept with the diagnostic | `Ingestion/FeatureSynthesizerTests.cs`, `Tool/IngestCommandTests.cs` | null | S3 |
| T10 | A failed step's `error` is its `FailureMessage` and `Failures.md` prints it; the HTML's comment is unchanged | `Ingestion/FeatureSynthesizerTests.cs`, new `Reports/DigestIngestedFailureTests.cs` (beside the `Digest*Tests`) | F8 | S3 |
| T11 | Cucumber steps carry their file name and line, and a failed step its `FailureMessage` | `Ingestion/Cucumber/CucumberFeatureSynthesizerTests.cs` | null | S3 |
| T12 | The member guard: `Error` moves from `KnownGaps` to `Carried` (`RequestResponseLogRoundTripTests.cs:70`), with a reason in `notOnAMarker` or set on a marker probe (`:113-118`, which goes red otherwise) | `Ingestion/RequestResponseLogRoundTripTests.cs` | the guard | S2 |
| T13 | In-process against ingest: the difference `IngestRoundTripTests.cs:102-105` allows (`error` on the failed send) is gone: `Assert.Empty(differences)` | `Ingestion/IngestRoundTripTests.cs` | the pinned gap | S2 |
| T14 | An external capturer's `error`: a string reaches `httpInteractions[].error`, `query http` and the `Failures.md` line; an object is read as its JSON text and the line is not malformed; `error` with no status draws `!Error`; `error` on a request stays there with the diagnostic | `Ingestion/InteractionRecordTests.cs`, `IngestPipelineTests.cs`, `Tool/QueryCommandTests.cs`, `Reports/DigestIngestedFailureTests.cs` | F6, F7 | S2 |
| T15 | `kronikol merge` keeps `error`, `attributionSource` and `expiredFrom` | new `Reports/Merge/MergeKeepsCallErrorsTests.cs` (beside `MergeSaysWhatItLostTests.cs`) | nulls | S2 |
| T16 | `kronikol export` sends a `!HttpRequestException` response as an ERROR span with the error as its message | `Kronikol.Tests.Otlp/OtlpSpanMapperTests.cs` | `Unset` | S2 |
| T17 | `FlowSpan` from an `Activity` and from an `OtlpSpan` keeps the seven fields; in-process segment data and every internal-flow golden are byte-identical after the builder and renderer move to it | `InternalFlow/*Tests.cs`, `Kronikol.Tests.Otlp` | the new type | S5 |
| T18 | Ingest with spans: the S0 spans file (checked in as a fixture) and interactions stamped with its ids give the GraphQL call a popup holding the resolver tree, a whole-test flow, no unclaimed startup span, and the diagnostics; without `--spans`, byte-identical to today | `Ingestion/IngestSpanTests.cs` (new), `Tool/IngestCommandTests.cs` | no spans path | S5 |
| T19 | The filters: supplied spans survive `AutoInstrumentation`; `Manual` narrows them; the static store is untouched by an ingest and a store span never reaches an ingested report | `Ingestion/IngestSpanTests.cs` | the .NET-only list; the static store | S5 |
| T20 | The inputs: a spans file inside an input directory is not read as interactions; `--strict` fails on a torn span line; a stray OTLP file among the inputs gets the one-line advice | `Tool/IngestCommandTests.cs` | malformed lines | S5 |
| T21 | Playwright: the ingested report's GraphQL arrow opens a popup whose activity diagram and flame chart name `graphql.resolve charge`, and the whole-test flow renders | `Kronikol.Tests.EndToEnd` | no popup | S5 |

---

## 5. Releases and bumps

`CLAUDE.md`: anything new for a consumer to call is a minor, and the highest-ranking change decides. Each slice adds
public surface (a record member, a flag, a request member, a type), so each is a **minor**. The order is the
roadmap's rule 1, shipped-and-wrong first: S4 fixes a claim false since 3.0.45, S3 a retry merge live on every
producer that writes one, then S2, then the largest, S5.

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| **R1** | S4: `--separate-setup`, the boundary, the corrections, Cucumber `KeywordType`, T1 to T6 | minor | a new flag; the boundary is a fix that changes output for library callers who set both options, called out |
| **R2** | S3: `attempt`, `sourceFile`, `sourceLine`, `--source-root`, the attempt split, `FailureMessage`, Cucumber step locations, T7 to T11 | minor | new record members and a new flag; the retry fix changes output for producers that already write two starts, called out |
| **R3** | S2: `error`, the two default rules, merge and export, the digest line, T12 to T16 | minor | a new record member; the digest line is new output for in-process runs too |
| **R4** | S5: `FlowSpan`, `IngestRequest.Spans`, `--spans`, the diagnostics, the wiki recipe, T17 to T21 | minor | a new public type, request member and flag |

The numbers are whatever the next minors are at execution (4.1.0 to 4.4.0 if nothing lands between). Per release,
in this order: the full suite green; the version in `Directory.Build.props`, `.claude-plugin/plugin.json` and
`.claude-plugin/marketplace.json`; the history action's `VERSION` and the template pins, as the last releases moved
them; the changelog entry stating which part moved and why; the wiki; commit; tag; push both; read the CI run of the
pushed commit before the next release.

**Kronikol4J.** It has no NDJSON writer and no ingest layer (`ROADMAP.md` D10; no Java file in this repository
reads NDJSON), so nothing ingest-only needs a ledger entry. Two do: R2's schema description (`Feature.sourceFile`),
and R3's digest line if the Java port ever gains `Failures.md` (it predates it: its rendering is .NET 3.0.43's, and
the digest arrived in 3.1.0). S5's move to `FlowSpan` must leave in-process output byte-identical, so it needs none.

---

## 6. Documentation

| Where | Change | Release |
|---|---|---|
| `CHANGELOG.md` | One entry per release, with the part that moved and why; R1 says the 3.0.45 claim was false and since when; R2 and R3 name the output that changes for existing producers | each |
| `../Kronikol.wiki/Ingesting-External-Captures.md` | the interaction table gains `error` (R3) and the `activityTraceId`/`activitySpanId` row says they join spans (R4); the tests vocabulary gains `attempt`, `sourceFile`, `sourceLine` and the retry rule (R2); the flag table gains `--separate-setup` (R1), `--source-root` (R2) and `--spans` (R4); the `--phase-from-steps` row (`:225`) and "Phases from steps" (`:375-385`) are corrected (R1); a "Spans" section (R4) | each |
| `../Kronikol.wiki/Ingesting-From-Jest.md` (new) | the S0 recipe and the owner's harness steps, linked from the ingest page and `Home` | R4 |
| `../Kronikol.wiki/Internal-Flow-Tracking.md` | an "Ingested runs" section: the span stream, the id contract, the filter rule | R4 |
| `../Kronikol.wiki/Phase-Aware-Tracking.md`, `Report-Configuration.md:123` | the ingested partition; the "first non-GIVEN BDD step" clause qualified | R1 |
| `../Kronikol.wiki/Generated-Reports.md` | `error` filled from ingest (`:265`); `Feature.sourceFile` no longer null for the tests NDJSON | R2, R3 |
| `../Kronikol.wiki/Exporting-to-OpenTelemetry.md` (`:32`), `HTTP-Tracking-Setup.md` (`:110-118`) | a `!Type` status exports as an error; `query http`'s address for a failed call is the response half's | R3 |
| `src/Kronikol/Reports/ReportGenerator.cs` schema text | `Feature.sourceFile` (`:6145`) | R2 |
| `plans/PLATFORM_FOUNDATIONS_PLAN.md` | G4 and F5 point here for the span stream; §12.4's phase-from-steps sentence corrected; P8's `Attempt` and source-location rows marked closed | R1, R2, R4 |
| `plans/JAVA_PORT_PLAN.md` App. C, `plans/NODE_PORT_PLAN.md` ingest appendix | the new fields, so a port carries them; the phase claim corrected | R1 to R4 |
| `plans/ROADMAP.md`, `plans/PLANS_STATUS.md` | the rows per release | each |

---

## 7. Where it sits in the roadmap

Stage 1d, next, at the owner's word (D31), ahead of every open stage; tracks A and C (1.14, 2.4, 2.5, 3) may run
beside it, and track B beside everything but R4. It takes from stage 14:

- from **14.1**, the pinned `Error` member: the round-trip gate starts with six known gaps, not seven;
- from **14.5**, the span stream's design (OTLP/JSON lines). 14.5 still freezes it, with `captureFormatVersion 1`,
  and still decides Go's asks C1 to C7; `attempt`, `sourceFile` and `sourceLine` join the tests contract before the
  freeze, which rule 6 wants;
- from **14.6** (F5), two of the foundations' P8 rows (the attempt and the source locations) and G4.

Stage 5's note ("Designed before 5.1, that contract would encode the pooling") is satisfied: 1c.4 shipped as 3.35.1
before this design.

---

## 8. Found on the way, not in this plan

Each is outside the five items and outside the ingest feed, or needs a measurement first. Placed as roadmap row 1.15
(rule 1: defects before features), each a patch unless marked.

| # | Defect | Level |
|---|---|---|
| 1 | Ten SDK tracking handlers leave a lone request when the send throws (no catch): CloudStorage, SQS, S3, BigQuery, DynamoDB, SNS, AtlasDataApi, StorageQueues, EventBridge, BlobStorage (e.g. `BigQueryTrackingMessageHandler.cs:95`); 3.18.0 fixed only HttpClient and Cosmos | READ |
| 2 | The component diagram counts only `HttpStatusCode >= 400` as errors, so `!Type` and SQL's `Error` are neither errors nor in its status distribution (`ComponentFlowSegmentBuilder.cs:199-211`) | READ |
| 3 | `TcpTap` swallows an upstream connect timeout silently: the linked token's cancellation is caught as "Shutting down" (`TcpTap.cs:212-216`, `:247-250`), with no log, counter or diagnostic | READ |
| 4 | `TestTrackingMessageHandler`'s implicit boundary fires once per handler instance: `_actionStartInjected` and `_wasInGivenSection` are fields never reset (`:28-30`, `:341-368`), so a client shared by several tests gives only the first its partition. Measure how adapters create handlers before fixing | READ, INFERRED |
| 5 | `StepTrackingOptions.WhenTriggersAction` is documented as "equivalent to `StartAction()`" but logs no marker, so a woven When step draws no partition | INFERRED |
| 6 | Four step-to-phase vocabularies disagree on And and But (`PhaseConfiguration.cs:47-54`, `StepCollector.cs:106-117`, `IngestAttribution.cs:419-426`, the handler) | READ |
| 7 | MSTest `[DataRow]` rows probably collapse into one scenario: the adapter de-duplicates on `class.method` (`DiagrammedComponentTest.cs:48`, `TestContextEnumerableExtensions.cs:22`) | INFERRED |
| 8 | `InternalFlowActivityListener` ignores its `additionalActivitySources` (`:44-55`); `InternalFlowActivitySources` is documented "null includes all sources" and is read only under `Manual`; `OpenTelemetryTrackingExtensions.cs:18` names a method that does not exist; `InternalFlowDiagramStyle.SequenceDiagram` renders an activity diagram | READ |
| 9 | `InternalFlowSpanStore.Clear()` clears its queue and its seen-set separately (`:39-43`): an `Add` between them can never be re-added | INFERRED |
| 10 | `kronikol query grep --in errors` does not search call errors, and `interactions --json` rows carry no `error` (track A, minor) | READ |
| 11 | `INGEST_FEED_PLAN.md` §6.1 says `InternalFlowTracking` changed no byte on the LightBDD comparison; the three differing diagrams' length gaps are each exactly 30 + 48 × the scenario's requests, the partition plus one `[[#iflow-…]]` wrapper per request, so both differed, and `--separate-setup` alone will not bring that comparison to 6 of 6 | INFERRED |
| 12 | Plan line references have drifted (foundations G4 `IngestPipeline.cs:284` is now 285; §11's `ReportDiagnostics :47` is now 67) | READ |

---

## 9. What is not known

- **Other runners and servers.** S0 measured Jest 30 with Fastify and Mercurius. Vitest and `node:test` load
  modules differently; Apollo Server and GraphQL Yoga need their own check of the GraphQL patch (INFERRED: the same
  `getModuleDefinitions` route applies to anything that imports `graphql`).
- **Node 24.** Jest's native `require(esm)` from Node 24.9 may change which modules its registry loads. Not measured.
- **Clocks under load.** 0 to 1 ms on an idle machine; the window allows 50. A loaded CI runner was not measured.
- **Other SDKs' OTLP/JSON.** Only JavaScript's output went through the reader (P4). The reader's own tests cover
  hex and base64 ids and both kind forms (`OtlpTraceReaderTests.cs:9-91`).
- **The Cucumber lane's earlier attempts' calls.** Each attempt there has its own `kronikol-test-id`; whether an
  earlier attempt's calls become a scenario of their own was not checked. T7's pipeline fact checks it for the
  tests lane; S3 checks the Cucumber lane before it ships.
- **How many producers write two starts for one test today.** S3's reading of them as retries is a fix for the case
  P3 measured; a producer that wrote a second start for another reason would see its test split. The diagnostic
  line says when it happened.

---

## 10. What this plan does not do

- **A Node package.** The harness stays the owner's code; the recipe documents it. `@kronikol/jest` is
  `NODE_PORT_PLAN`'s, after the foundations.
- **A Firestore adapter.** No lane has one. A client wrapper that writes records, as the owner's does, captures the
  documents; OpenTelemetry would give arrows without them.
- **The options file** (F4) and #111's second half, the rest of the toggle defaults.
- **The freeze.** `captureFormatVersion` and the header record are 14.5's.
- **`FailureCause` and `ExampleDisplayName`** on the tests records, and **V8 stack frames** for the digest's
  `Thrown at`, which parses .NET frames only (`FailureText.cs:215-251`; foundations L6's `frames[]`).
- **Spans as arrows** (the OTLP tail through `--spans`), and span attributes or events in the popup.
- **7.1**, `kronikol query scenarios --json` projecting the source location, which stays in stage 7.
- **The defects of §8.**

---

## 11. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | The span stream's format: OTLP/JSON lines, or a Kronikol record per span | **OTLP/JSON lines.** Every SDK writes it, the reader exists and read the spike's file whole (P4), and the foundations and the Go plan already chose it. A Kronikol record would make every capturer write a converter, and 14.5 can still add a header line to it |
| Q2 | Do `--spans` also become arrows (the tail) | **No.** The harness already captures its calls; the one span the tail would draw duplicated one (P4). A later `--spans-as-calls` can add it with `--merge-duplicates` |
| Q3 | Ingested spans and the span filter | **`Full` unless `Manual`.** The default list is .NET names and drops every Node span; the capturer chose what to export |
| Q4 | Where the reader lives | **In the OTLP package, read by the tool**, with `IngestRequest.Spans` in memory for library callers. Moving it into core is F6's question |
| Q5 | Retries | **The Cucumber lane's rule:** the last attempt is the scenario, earlier ones a `retry N` label, their calls left out and counted; a second `start` without `attempt` is a retry. Showing earlier attempts' calls (a collapsed group) is a later feature, and the flaky verdict in history is where the attempt story is told today |
| Q6 | Source paths | **Relative to `--source-root`, default the current directory**; others kept with one diagnostic |
| Q7 | Where the boundary comes from | **The steps first, then the calls' phases; only when `SeparateSetup` is on; the capture's own marker, or its own step bars, win** |
| Q8 | `error` without a status, and on a request | **`!Error` for the first; kept on the request with a diagnostic for the second.** A non-string `error` is read as its JSON text |
| Q9 | Print a failed call's error in `Failures.md` | **Yes, in S2.** The digest is what an agent reads first, and it says a call failed without saying why |
| Q10 | Four minors or one | **Four, in the order of §5.** Each is testable and releasable alone, and the first two fix live defects |
| Q11 | The defects of §8 | **Roadmap row 1.15**, by rule 1, with items 4, 5 and 7 measured before they are fixed |

---

## 12. Log

Each release records here what shipped, what the measurements were, and what differed from this plan.

### R1: 4.1.0 (S4), 2026-09-30

Shipped: `kronikol ingest --separate-setup`; `IngestPipeline` synthesises each test's Setup/Action boundary when
`SeparateSetup` is on, from the steps first and the calls' phases second, skipping a test whose capture carries its
own `Phase` marker or its own step bars, and adding none from the steps when no call of the test came before the
boundary; the Cucumber lane's step markers carry the pickle step's type as `keywordType`; the false documentation
corrected in the option's and `ApplyPhaseFromSteps`'s doc comments, `SeparateSetup`'s and `HighlightSetup`'s, the
usage, four wiki pages (Ingesting External Captures, Phase Aware Tracking, Report Configuration, Diagram
Customisation) and three plans (the foundations' §12.4, the Java plan's App. C, the Node plan's ingest appendix).

Proofs, each red first: T1 (`IngestCommandTests.Separate_setup_flag_reaches_the_report`, red on the unknown option);
T2 to T4 (`IngestSetupBoundaryTests`, four facts: from the steps, from the phases, a first When step, the capture's own
marker; and `IngestRoundTripTests.A_store_whose_run_drew_no_boundary_…`); T5
(`CucumberIngestPipelineTests.A_german_features_steps_…`, red on `Unknown` phases); T6 (Playwright,
`IngestedSetupPartitionTests`, the `Setup` label and the `#F6F6F6` fill in the rendered SVG, red with the boundary
step removed). Three mutations, each caught by the fact written for it: without the step-bar skip the round-trip
fact goes red, without the `Phase`-marker skip the capture-wins fact, without the `SeparateSetup` gate the phases
fact (two identical calls stop collapsing into `loop ×2`). Core suite: 6,359 passed, 7 skipped.

What differed: T2 to T4 are in a file of their own, `Ingestion/IngestSetupBoundaryTests.cs`, rather than in
`IngestPipelineTests.cs`. T4's round-trip half is a fact of its own (a store whose run drew no boundary, with phased
calls and step bars), since the existing theory's fixture carries a `Phase` marker and would pass whatever the rule.
The phases route places its boundary immediately before the chosen request in the list rather than at the front, so a
record at the same instant that came before it (a Setup call's response) stays on the setup side; the steps route
places it at the front as planned. To run Playwright in the plan's container, Chromium needed the proxy's CA in its
NSS store and a browsers folder mapping Playwright 1.59's expected build onto the installed one (no repository
change).

Not done from the session, for want of access: the tag `v4.1.0` (on `03b52b7c`) was refused by the session's git
proxy (HTTP 403 on `refs/tags/*`; the push of `main` went through), so the Release workflow, which runs on a tag, has
not published 4.1.0; and the wiki commit could not be pushed (`lemonlion/Kronikol.wiki` is not in the session's
repositories). It is `INGEST_FIDELITY_PLAN.harness/wiki/0001-…patch`, with how to apply it.
