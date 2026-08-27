# OTLP Export Plan — Kronikol captures → the user's tracing backend

Make Kronikol's captured interactions exportable as OpenTelemetry spans over OTLP/HTTP, so the
traffic Kronikol alone can see — proxy-tap and TCP-tap hops from services that emit no telemetry,
handler-captured calls with test attribution — appears in Tempo/Jaeger/any collector next to the
app's real traces. This is the outbound twin of `Kronikol.Extensions.Otlp`'s receiver-tee, and it
makes the wiki's existing (currently dangling) cross-reference true: `Integration-Otlp-Extension.md`
already points readers at "the other direction: exporting Kronikol's own in-process tracking as
OTel spans", which no page delivers today.

## Shape of the feature

Three layers, each useful alone, built bottom-up:

1. **Pure mapping + encoding** — `RequestResponseLog` pairs → OTLP `ExportTraceServiceRequest`
   JSON. Deterministic, no I/O; the in-repo `OtlpTraceReader` is the round-trip oracle.
2. **`OtlpExporter`** — batch push of an `IEnumerable<RequestResponseLog>` to an endpoint
   (post-hoc export: at end of run, or from the CLI over NDJSON). This is the primary mode:
   test suites are batch-shaped, and post-hoc export is trivially D3-safe (nothing on any hot path).
3. **`OtlpExportSink : IRequestResponseSink`** — streaming wrapper for live tap topologies
   (ProxyTap/TcpTap/OtlpTap `Sink` properties, composed via `CompositeRequestResponseSink`),
   with the bounded-queue/drop-counter discipline the taps already enforce.

Plus a new CLI verb: `kronikol export`.

**Placement: inside the existing `Kronikol.Extensions.Otlp` project.** Both OTLP directions in one
package; the encoder reuses the reader's message-shape knowledge for round-trip tests; and the
project stays dependency-free (`HttpClient`, `System.Text.Json`-free hand-writing or manual
StringBuilder JSON — see decision 2). NuGet description gains the export sentence.

## Design decisions

1. **OTLP/JSON first; protobuf encoding deferred.** Collectors accept OTLP/JSON on `/v1/traces`
   (`Content-Type: application/json`) — it is what our own tap parses. A hand-rolled protobuf
   *encoder* (symmetric to `OtlpTraceReader`) is a well-understood follow-on (M8, optional) if a
   user hits a JSON-rejecting endpoint; do not build it speculatively.
2. **Encoder writes JSON by hand** (StringBuilder + the escaping rules the codebase already uses),
   matching the repo's dependency-free stance and giving byte-stable output for golden tests.
   Ids are lowercase hex (`traceId` 32, `spanId` 16), timestamps `…UnixNano` as decimal strings —
   exactly the forms `OtlpTraceReader` accepts.
3. **Span identity preserves D4 (real trace ids win), and tests group as traces.**
   - `traceId` := `log.ActivityTraceId` when present — exported spans land in the *same*
     distributed trace the SUT emitted, which is the whole point.
   - When no Activity id was captured, note the trap: `RequestResponseLogger.LogPair` and the
     handlers mint a **fresh `TraceId` Guid per pair**, so a naive Guid→hex mapping floods the
     backend with thousands of single-span traces. Instead, `TraceIdStrategy` (default `PerTest`)
     derives the trace id deterministically from `TestId` (MD5 → 32-hex, same recipe as
     `InteractionRecord.ToGuid`) so **one test renders as one trace** in Tempo/Jaeger — the
     Kronikol mental model. `TraceIdStrategy.PerPair` keeps the raw `log.TraceId` for users who
     want it.
   - `spanId` := `log.ActivitySpanId` when present; else the first 16 hex of
     `log.RequestResponseId` (`N` format). Deterministic — re-export produces identical spans.
   - **No `parentSpanId` in v1** — exported traces are flat (a fan of spans under no root).
     Inferring parent/child from caller-name chains + interval nesting is a documented future
     enhancement, not v1 scope; state this in the wiki page so nobody reads flatness as a bug.
   - **Null timestamps**: `RequestResponseLog.Timestamp` is `DateTimeOffset?`. Rule: a null
     request time borrows the response's; both null → stamp export time and add
     `kronikol.times.synthetic = true`. Never drop a record over a missing timestamp.
4. **Echo suppression by default.** Records with `CapturedBy == InteractionMerger.SpanSource`
   *came from* the backend's own telemetry (via `OtlpTap`); re-exporting them duplicates spans the
   backend already stores. Skip them unless `IncludeSpanSourced = true`.
5. **Also skipped, always**: `IsDiagramMarker` records (rendering control, not telemetry) and
   `TrackingIgnore` records.
6. **One span per request/response pair.** The request record supplies start time + request
   attributes; the response supplies end time + status. Pairing key: (`TraceId`,
   `RequestResponseId`). Batch mode pairs from the full list (same-key, opposite `Type`);
   streaming mode buffers pending requests with a TTL (default 30 s), exporting an orphan as a
   zero-duration span with `kronikol.orphan = true` when the TTL lapses. A response with no
   buffered request exports the same way.
7. **Mapping table** (span `name` = the Kronikol method label; `kind` = `CLIENT`, or `PRODUCER`
   when `MetaType == Event`):

   | Span field / attribute | Source |
   |---|---|
   | resource `service.name` | `CallerName` (spans grouped into one `resourceSpans` entry per caller) |
   | scope name | `Kronikol` (+ assembly version) |
   | `url.full` | `Uri` |
   | `http.request.method` | `Method` when it is an `HttpMethod` |
   | `http.response.status_code` | numeric `StatusCode`; span status `ERROR` when ≥ 400 (client-span semconv rule), or when `StatusCode` is the string form of a failure |
   | `db.system.name` | reverse of `SpanToInteractionMapper.CategoryFor` where `DependencyCategory` maps to a db system; otherwise omitted |
   | `peer.service` | `ServiceName` |
   | `kronikol.test.id` / `kronikol.test.name` | `TestId` / `TestName` |
   | `kronikol.phase` | `Phase` when not `Unknown` |
   | `kronikol.dependency.category` | `DependencyCategory` when set |
   | `kronikol.captured.by` | `CapturedBy` when set |
   | `kronikol.request.body` / `kronikol.response.body` | request/response `Content` — **only** when `IncludeBodies = true`, capped at `BodyAttributeCapBytes` (default 8 KiB) with the standard `…truncated` marker; the cap guards collector attribute limits. |
   | headers | **not exported** in v1 (size + secret risk outweighs value; revisit behind an allow-list option if asked) |

   Attribute names reuse the `kronikol.*` keys the taps' own Activities already emit
   (`kronikol.test.id`, `kronikol.caller`, `kronikol.service` in `ProxyTap`/`TcpTap`) — one
   vocabulary across everything Kronikol puts on a span.

   **Redaction is NOT uniformly "already done" — each path must be audited, and one needs work:**
   - Batch export from the in-process store: `RequestResponseLogger.Redaction` ran in `Log()`. Safe.
   - Streaming sink fed by taps: `ProxyTap` redacts via `SecretDenylist` and `TcpTap` via its
     `Key/Value/DocumentRedaction` hooks *before* the sink. Safe, document the dependency.
   - **CLI export from NDJSON: nothing has run** — `RequestResponseLogger.Redaction` only applies
     on the ingest-replay path (`IngestCommand` sets it around the replay, default ON). The
     `export` verb must therefore apply `CaptureRedaction` itself — default ON, with `--no-redact`
     and `--redact-header` flags mirroring `IngestCommand` exactly.

8. **Non-interference invariant (the most-documented commitment in the repo).** The exporter is a
   standalone `HttpClient` POSTing to a URL. It never touches the SUT's `TracerProviderBuilder`,
   never registers processors, never flips `Activity.Recorded`. Document this prominently.
9. **D3 for the streaming sink.** `Log()` is `TryWrite` into a bounded channel (default capacity
   4096); a background task batches (`BatchMaxSpans` default 512, `FlushInterval` default 2 s) and
   POSTs; failures and drops are counted, never thrown, never block. `Diagnostics()` returns
   `DiagnosticKind.CaptureDegraded` entries mirroring `OtlpTap.Diagnostics()`. `FlushAsync()` +
   `DisposeAsync()` drain with a timeout for deterministic test-end export.
10. **No retries in v1** beyond one immediate re-attempt per batch; a test-run exporter that
    retries aggressively holds the process open. Count failures; the diagnostics surface tells the
    user their collector was down.

## New/changed files

```
src/Kronikol.Extensions.Otlp/
  OtlpSpanMapper.cs          RequestResponseLog pair → OtlpExportSpan (pure; pairing helpers)
  OtlpJsonEncoder.cs         OtlpExportSpan[] → ExportTraceServiceRequest JSON (pure)
  OtlpExporter.cs            batch push: pair → encode → gzip? → POST, paging by BatchMaxSpans
  OtlpExportSink.cs          IRequestResponseSink + IAsyncDisposable streaming wrapper
  OtlpExportOptions.cs       Endpoint, Headers, IncludeBodies, BodyAttributeCapBytes,
                             IncludeSpanSourced, QueueCapacity, BatchMaxSpans, FlushInterval,
                             PendingRequestTtl, Gzip, Log, Name + Validate()
src/Kronikol.Tool/
  ExportCommand.cs           the new verb
  Program.cs                 add "export" to the switch + top-level help
tests/Kronikol.Tests.Otlp/
  OtlpSpanMapperTests.cs, OtlpJsonEncoderTests.cs, OtlpExporterTests.cs, OtlpExportSinkTests.cs
tests/Kronikol.Tests/Tool/
  ExportCommandTests.cs
```

CLI surface:

```
kronikol export --otlp <endpoint> [--header k=v]... [--include-bodies] [--body-cap N]
                [--include-span-sourced] [--per-pair-traces] [--no-redact] [--redact-header h]...
                [--gzip] [--dry-run --out file.json]
                <captures.ndjson>...
```

Reads via `NdjsonInteractionReader` → `InteractionRecord.ToLog` → `OtlpExporter`. `--dry-run`
writes the encoded JSON instead of POSTing (testable without a listener; doubles as a debug tool).
Prints spans/traces/skipped/orphans counts; exit codes follow `IngestCommand` conventions.

## Test plan (TDD — red first at every step)

The killer property this repo uniquely enables: **the decoder and the receiver are already in the
same package.**
- **Round-trip oracle**: encode logs → `OtlpTraceReader.Read` → assert span fields. Every encoder
  test is a decode-back assertion, not a string-compare (plus a handful of byte-stable golden
  strings for format lock-in).
- **Full-circle integration**: `OtlpExporter` POSTs to a live `OtlpTap` (port 0) with
  `CaptureKinds` widened; assert the tap's sink receives logs whose uri/status/test-id round-trip
  the originals. Assert **semantic equivalence, not equality**: the tap's mapper rebuilds labels
  from semconv attributes, so a `Find ← Trial` label round-trips as whatever the mapper derives
  from `db.system.name`/`db.operation.name` — pin the expected derived values, don't diff the
  originals. One test, enormous confidence.
- Mapper: pairing (in-order, out-of-order, orphan request TTL, orphan response), echo suppression,
  marker/`TrackingIgnore` skip, id derivation vectors (Activity ids win; Guid-derived hex is
  deterministic and matches `InteractionRecord.ToGuid`'s inverse expectations), status→ERROR rule,
  `MetaType.Event` → `PRODUCER`, category→`db.system.name` reverse map, body opt-in + cap +
  truncation marker.
- Sink D3: endpoint that never responds — `Log()` returns immediately, drops counted at capacity,
  `Diagnostics()` reports degradation; disposing flushes what it can within the timeout.
- CLI: mirror `IngestCommandTests` style — `--dry-run` output golden, bad flags, missing files,
  counts on stdout.
- Per CLAUDE.md: any bug found in surrounding code during this work gets its own red-green fix.

## Milestones

- **M1 — mapper.** `OtlpSpanMapper` + options validation. Pure, fully unit-tested.
- **M2 — encoder.** `OtlpJsonEncoder` with decode-back tests + format goldens.
- **M3 — batch exporter.** `OtlpExporter` against a stub listener; gzip; paging; header auth
  (the tap's `ExpectedHeaders` model in reverse). Full-circle test vs `OtlpTap` lands here.
- **M4 — CLI verb.** `ExportCommand` + `Program.cs` + help text + tests.
- **M5 — streaming sink.** `OtlpExportSink` with channel/batcher/TTL/diagnostics/D3 tests; wire-up
  documentation for the three tap `Sink` properties via `CompositeRequestResponseSink`.
- **M6 — docs.** New wiki page `Exporting-to-OpenTelemetry.md` (what exports, what's skipped and
  why, echo suppression, trace-grouping strategy + flat-trace statement, body policy, redaction
  per path, non-interference statement, tap composition example, CLI) + `_Sidebar.md` entry;
  **fix `Integration-Otlp-Extension.md`'s cross-reference** to point at the new page (it currently
  mis-cites the OpenTelemetry extension page); README extensions table; nuget-readme.
- **M7 — release.** Full suite green → bump **all** packages to the next patch version,
  CHANGELOG entry, commit, tag `v{version}`, push commit + tag (per CLAUDE.md).
- **M8 (optional, demand-driven) — protobuf encoder.** Hand-rolled writer symmetric to
  `ProtobufReader`; round-trip through `OtlpTraceReader`'s protobuf path as the oracle;
  `--protobuf` CLI flag / `Protocol` option.

## Non-goals / boundaries to record

- **Not** an OTel SDK integration: no `TracerProvider`, no processors, no `Activity` re-emission.
- Kronikol4J divergence: export is .NET-only for now — note it in Kronikol4J's
  `docs/REMAINING_PARITY.md` when this ships (the Java side gains the *inbound* tap under
  `docs/OTLP_TAP_PLAN.md` first; export there is a later follow-on).
- Header export deferred; body export opt-in; both decisions revisitable with user demand.
- The D-invariants cited in code comments (D3/D4) have no written definition anywhere in the repo —
  while touching these files, add a short `docs`/wiki note defining D3 (capture never blocks or
  degrades the observed system) and D4 (real W3C ids are preserved end-to-end), since this plan
  leans on both.
