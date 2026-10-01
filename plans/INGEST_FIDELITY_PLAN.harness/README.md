# INGEST_FIDELITY_PLAN harness

What `INGEST_FIDELITY_PLAN.md` measured before it was written, on 2026-09-30, at 4.0.0 (`00abca6`).

`research-notes.md` holds the four code-reading passes the plan summarises (file and line maps per item, the tests
that will flip, the insertion points) and how to set up a container to execute a slice.

## `otel-jest/`: S0, OpenTelemetry under Jest

The question item 5 of the plan was gated on: can a test harness get a Node service's internal spans when the
service runs in-process under Jest, without changing the service? The service (`src/app.js`) stands in for the
Fastify + GraphQL service of the owner's picture: Fastify 5, Mercurius 16, one resolver that calls a payment
provider faked by MSW, a risk service that is a real local fake (MSW passes it through), and an internal async
function. The harness (`harness/`) is an in-memory tracer provider, a span processor that stamps the running
test's id from the harness's `AsyncLocalStorage` on every span it starts, the MSW fakes with a
`request:start` listener, and a Fastify `onRequest` hook of the harness's own.

Run it with `./run.sh` (Node 22 and npm). Versions are pinned in `package.json`; `results/versions.txt` records
what ran. `results/` holds the output of the recorded run, with the harness folder's path replaced by `<spike>`.

| Variant | Setup | Spans | What it shows |
|---|---|---|---|
| V1 `tests/v1-auto.test.js` | `registerInstrumentations()` with the http, undici, GraphQL and Fastify instrumentations, then load the service: the documented setup | **1**: the undici span of the passed-through call | OpenTelemetry's auto-instrumentation hooks Node's `require`; Jest loads modules through its own registry, so GraphQL is never patched. `@fastify/otel` is a plugin nobody registered. Undici works because it listens on a diagnostics channel, which is process-wide. The harness's hooks see no active span |
| V2 `tests/v2-harness.test.js` | `@fastify/otel` with `registerOnInitialization: true` (Fastify announces each new instance on the `fastify.initialization` diagnostics channel), the GraphQL instrumentation's patches applied through Jest's own `require` via `getModuleDefinitions()`, undici | **16**: 11 GraphQL (parse, validate, the operation, every field resolver, nested ones included), 4 Fastify (the request, its hooks, the handler), 1 undici | Everything the service does, with no change to it. The harness's Fastify hook sees the request's span; the MSW listener runs inside `graphql.resolve charge`, same trace. Every span of the request carries the test's id. `results/v2-spans.otlp.jsonl` is the same spans serialised by `JsonTraceSerializer` as one OTLP/JSON `ExportTraceServiceRequest` per line |
| V3 `tests/v3-concurrency.test.js` | V2's setup, two tests' requests in flight at once | **31** in 3 traces | No trace holds spans of two tests (`mixedTraces: 0`); each MSW event saw its own test's trace and id |
| V4 `tests-setupfiles/v4-setupfiles.test.js` | V2's setup in `harness/setup.js` (Jest `setupFiles`) and `harness/teardown.js` (`setupFilesAfterEnv`); the test file has no tracing code | **16** | The realistic form: a harness change, no test changes |

Found on the way, each written into the plan:

- **The patches must land before the service loads.** Mercurius destructures `execute`, `parse` and `validate`
  from `graphql` when it is required, so patching after that changes nothing. `setupFiles` runs in the test
  file's registry before the file, which is the right moment.
- **`registerOnInitialization` leaks across test files.** The diagnostics channel is process-wide, so a
  subscription left by one test file registers a second plugin on the next file's Fastify instances in the same
  worker: `FastifyError: The decorator 'opentelemetry' has already been added!` (seen on the first full run).
  `disable()` unsubscribes; the harness calls it in `afterAll`.
- **The clocks agree.** The harness's `Date.now()` in its Fastify hook and the request span's start differed by 0 to
  1 ms over 15 observations (`wallClockMs` and `spanStartMs` in `results/`); internal flow allows 50 ms.
- **An MSW-mocked call has no client span.** MSW answers before undici sees the request, so only the
  passed-through call has an undici span. The mocked call is in the diagram anyway, from MSW's events, and its
  listener can stamp the active span's ids on the interaction record, so the popup can still find the call's
  spans by trace.
- **Running this service under Jest on Node 22 at all** needed three things unrelated to OpenTelemetry: MSW
  pinned to 2.11 (2.12 and later depend on `rettime`, which is ESM-only), `content-disposition` overridden to
  0.5.4 (3.0.0, which `@fastify/static` 10 pulls, is ESM-only), and `--experimental-vm-modules` (Mercurius 16
  loads a dependency with a dynamic `import()`). Jest supports `require(esm)` natively from Node 24.9. A suite
  that already runs under Jest has solved these already.

## The .NET probes

`IngestFidelityProbeTests.cs` (P1 to P3) was a temporary xUnit class compiled into
`tests/Kronikol.Tests/Ingestion/`, and `IngestFidelitySpanProbeTests.cs` (P4) one in `tests/Kronikol.Tests.Otlp/`. Each
was run alone with the .NET 10.0.401 SDK (`dotnet test <project> --filter "FullyQualifiedName~IngestFidelity"`, with
`KRONIKOL_PROBE_OUT` naming the output file) and deleted from its project. `probe-results.txt` is their output.

| Probe | Feeds | Shows |
|---|---|---|
| P1 | a tests file with a Given step then a When step, a call in each, four option sets | `--phase-from-steps` tags the calls and draws no partition; one `Phase` marker line does |
| P2 | a failure half with `statusCode: "!TypeError"` and an `error` string; a response with an object in `error` and no status | the status survives (diagram, `Failures.md`), the text is dropped; the object is ignored without a diagnostic |
| P3 | one test id, a failed attempt then a passing one; the same with an id per attempt | one passed scenario with the first attempt's error and both attempts' steps and calls; or two scenarios and a run that reads failed |
| P4 | `otel-jest/results/v2-spans.otlp.jsonl` through `OtlpTraceReader.ReadJson` and `SpanToInteractionMapper` | all 16 spans with their parent tree, kinds, scopes and test ids; the tail mapper would draw one arrow, the call MSW passed through |

To repeat: copy a probe back into its project, run the filter, read the output file, delete the probe.

## R4's parity probe: internal flow on `FlowSpan`, in-process output unchanged

R4 moved internal flow from `System.Diagnostics.Activity` to `FlowSpan`, and the plan asked for an in-process report
that does not move a byte. `FlowSpanParityProbeTests.cs` was a temporary xUnit class in
`tests/Kronikol.Tests/InternalFlow/` that, with `KRONIKOL_PROBE_OUT` set, writes two in-process reports with every
internal-flow surface on: three tests of three traced calls each (nested spans, one call with no trace id per test, one
span no call claims), Manual granularity with its own `ActivitySource`, the activity-diagram and call-tree styles, the
flame chart, the whole-test flow as both, the component diagram and the mergeable data file, payloads uncompressed.

`paritydiff.py <before> <after>` compares two such folders file by file, after inflating every gzip-and-base64 blob
inside them and renaming the run's random values (GUIDs, trace and span ids, timestamps) by order of first
appearance; the scenario search index, binary and holding the run's ids, is left out. Two runs of the probe on 4.3.0
(`acb80f0`) diffed to nothing; a run on 4.3.0 against a run with the refactor diffed to nothing in all 24 files; a run
with the span tree broken (no node a child) diffed in 6 of them, so the comparison sees a change in the drawing.

To repeat: copy the probe back into its folder, run `dotnet test tests/Kronikol.Tests --filter
"FullyQualifiedName~FlowSpanParityProbeTests"` once per build with `KRONIKOL_PROBE_OUT` naming a fresh folder, run the
script on the two folders, delete the probe.
