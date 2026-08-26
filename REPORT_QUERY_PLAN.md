# Making Kronikol reports debuggable by an LLM

**Status:** implemented in full in 3.0.47. Kept as the design record; `QUERY_V2_PLAN.md` builds on it.

**The problem.** An agent asked to debug a test run reads `TestRunReport.json` and burns its whole
context. A real report measured on 2026-08-22 (`sidekick-intelligence-e2e/.logs/kronikol/`) was 10.7 MB
≈ **2.7M tokens**, with a single embedded diagram of 663 KB ≈ **166k tokens** — one diagram larger than
most context windows.

**The finding that reframes it.** The fix is not only "give agents a smaller way to read the file". The
JSON writer is discarding data the HTML writer uses, so for several questions — most importantly *why did
this assertion fail* — the JSON does not contain the answer at any size. Two of the three work items below
are about closing that gap; the query tool is the third.

**Three work items, in dependency order:**

| | Work item | Where | Shape |
|---|---|---|---|
| **A** | Close the export gaps | `src/Kronikol` (report generation + a little capture) | ~8 focused changes, all additive |
| **B** | `kronikol query` subcommand | `src/Kronikol.Tool` | New command surface |
| **C** | `kronikol-test-debugging` skill | `templates/skills/` + wiki | Docs + a fallback script |

---

# Part 1 — Findings

## 1.1 Size anatomy

Measured on the 10.7 MB report (4 features, 19 scenarios, 1202 interactions, 19 diagrams, 62
attachments, **all 19 passed**):

| Section | Size | Share |
|---|---|---|
| `diagrams` | 4.22 MB | 51% |
| `httpInteractions` | 4.05 MB | 49% |
| `steps` (including every assertion and its result) | 34 KB | **0.4%** |
| `attachments`, `diagnostics`, names, ids, results | ~20 KB | 0.2% |

> **Caveat on the evidence.** That file was regenerated on 2026-08-23 by a later run (now 160 KB, 20
> interactions, tracking largely not attached), so these figures cannot be re-derived from that path.
> They are recorded here as measured. Everything in §1.3–§1.5 comes from the source, not the sample,
> and is re-checkable at any time.

Derived measurements from the same file:

- Breaking the 663 KB diagram into note blocks: **73 payload notes = 653,239 bytes**, 11 assertion notes
  = 537 bytes, 4 step bars = 0. Structure + assertions + step bars ≈ 12 KB, **1.8% of the diagram**.
  Across all 19 diagrams: 3.81 MB → ~95 KB (2.6%).
- Payload duplication *within* `httpInteractions`: 562 interactions carry a body, **90 unique**.
  3.17 MB → 0.53 MB deduplicated; one 234-byte body appears 28 times.
- 533 of 562 bodies are JSON, so JSON-aware slicing applies to nearly all of them. 8 carry a capture-time
  truncation marker.

## 1.2 A report is four layers, three of them tiny

1. **Narrative** — features → scenarios → steps → sub-steps (the assertions). 34 KB for the whole run.
2. **Topology** — who called whom, in what order, with what status. The biggest diagram expresses this
   in **44 arrow lines / 1.5 KB**.
3. **Payloads** — bodies and headers. 8+ MB, stored twice (see §1.3) and ~6× duplicated internally.
4. **Artifacts** — attachments, diagnostics. ~20 KB.

Layers 1, 2 and 4 are the navigational layer and total 2–3% of the file. Layer 3 must be pull-on-demand.

## 1.3 Diagram notes are a *rendering* of payloads, not a copy

The note attached to an arrow is the output of a pipeline over `content`
([`PlantUmlCreator.FormatNoteContent`](src/Kronikol/PlantUml/PlantUmlCreator.cs#L246)):

| Stage | Effect |
|---|---|
| phase variant ([:205-210](src/Kronikol/PlantUml/PlantUmlCreator.cs#L205-L210)) | `activeVariant.Content` **replaces** the content; `Skip: true` drops the interaction from the diagram |
| pre / mid / post formatting processors | three arbitrary user `Func<string,string>` hooks, the last applied to the rendered note |
| binary detection | body becomes `[binary content]` |
| GraphQL `FormattedQueryOnly` | body reduced to the query, headers suppressed |
| `FocusFields` → `JsonFocusFormatter` | deliberate subset of fields |
| `excludedHeaders` / `excludeAllHeaders` | headers filtered and re-sorted |
| `WrapUnbreakableRuns`, JSON pretty-print | same data, different bytes |
| `TruncateNoteContent(…, truncateNotesAfterLines)` | a **line** cap, independent of the capture-time **byte** cap on `content` |
| `CollapsedCount > 1` | consecutive identical calls collapse to one arrow; the siblings get no note |

**Direction of the difference:** usually `content` is the superset and the note is a lossy view of it. But
with phase variants, or a formatting processor that *adds* information (decoding, decompressing,
annotating), **the note can hold data that exists nowhere in `httpInteractions`** — and the note is what
the user saw in the HTML report, so it is what they will quote back at you.

**Design consequence:** read payloads from `httpInteractions` by default, but keep an addressable
`query note` path, and *detect* divergence rather than assuming it away (§3.4).

## 1.4 The export gap audit

Report generation writes the same in-memory data through two writers. The HTML writer reads nearly
everything; the JSON writer picks a subset:

```
RequestResponseLog[] + Feature[]  (in memory at end of run)
        │
        ├──> PlantUmlCreator ─────> diagrams ──> TestRunReport.html      reads ~all fields
        └──> MapLogJson / MapStepJson ─────────> TestRunReport.json      writes a subset
```

### (a) Interaction fields — on the record, not exported

`MapLogJson` ([ReportGenerator.cs:3103](src/Kronikol/Reports/ReportGenerator.cs#L3103)) emits 11 fields.
`RequestResponseLog` also carries:

| Field | What its absence costs a debugger |
|---|---|
| `MetaType` (`Default`/`Event`) | events are indistinguishable from request/response |
| `DependencyCategory`, `CallerDependencyCategory` | whether `redis` is a cache and `mongo` a database — drives participant shape and arrow colour in the diagram |
| `Phase` (`Setup`/`Action`) | whether a call happened during setup or the action under test |
| `IsUserAction` | a UI click/navigate vs a real dependency call |
| `ActivityTraceId`, `ActivitySpanId` | **the bridge to OTel traces and application logs** — `traceId` in the JSON is Kronikol's own GUID, not the W3C id |
| `CapturedBy` (`wire`/`span`) | which capture path produced the record; matters when capture fidelity is the bug |
| `SetupVariant` / `ActionVariant` | when configured, the diagram renders different content than `content` |

Not on the record at all, and worth deriving: **per-interaction duration**, available from the
request/response timestamp pair (`ComponentFlowSegmentBuilder` already computes it that way).

### (b) Assertion detail — in `Track`'s hands, never stored

[`Track.LogAssertion`](src/Kronikol/Tracking/Track.cs#L417) receives `expression`, `resolvedValues`,
`failureMessage`, `callerFilePath`, `callerLineNumber`. It then:

- calls `StepCollector.AddAssertionSubStep(testId, text, passed)` — **text and a boolean**
  ([:429](src/Kronikol/Tracking/Track.cs#L429)); the text *does* include the resolved values, because it
  is `AssertionExpressionFormatter.Format(expression, resolvedValues)`;
- renders `✗ label\n{failureMessage}` plus a source-location comment `'__^*__:File.cs:L42` into a PlantUML
  string ([:446-458](src/Kronikol/Tracking/Track.cs#L446-L458)).

So **the failure message and the source location reach the diagram and nothing else.** They are the two
things most wanted when a test fails. And when `IncludeTrackedAssertionsInStepList` is off, assertions do
not reach `steps` at all — the diagram becomes their only home.

### (c) Step detail — implemented, but wired only to the mergeable file

`ScenarioStep` ([ScenarioStep.cs](src/Kronikol/Reports/ScenarioStep.cs)) carries `Parameters`
(inline values, **tabular values with columns and rows**, tree values), `TextSegments`, `BypassReason`,
`Comments`, `DocString`, `DocStringMediaType`.

| Mapper | Used by | Emits |
|---|---|---|
| `MapStepJson` | **`TestRunReport.json`** | keyword, text, status, durationSeconds, subSteps, attachments |
| `MapStepJsonFull` | mergeable JSON only | the above **+ Parameters + TextSegments** |
| neither | — | `BypassReason`, `Comments`, `DocString`, `DocStringMediaType` |

`GenerateMergeableData` defaults to **false**, so in the default configuration the richer mapper never
runs. A parameterised test's inputs — the Gherkin data table, the example row values — are absent from the
file an agent reads, although the code to serialise them exists and is tested.

### (d) Run-level data — mergeable only

`componentRelationships`, `internalFlowSegments` (the activity/span data behind "why is this slow"),
`wholeTestFlow` and `ciMetadata` are written only to the mergeable JSON. The standard file has none of it.

### (e) Diagram-only content

- **Tabular input row markers** (`hnote across #lightyellow : Row 3`,
  [TabularInputs.cs:84](src/Kronikol/TabularAttributes/TabularInputs.cs#L84)) — which parameterised row
  the following calls belong to.
- Any user-injected fragment via `Track` / `DefaultTrackingDiagramOverride.InsertPlantUml`.

Both arrive as marker records whose payload is the `PlantUml` property — which `MapLogJson` never emitted.
(The markers themselves were removed from `httpInteractions` on 2026-08-22, where they had been exporting
as content-free calls to `http://override.com/`: 424 of 1202 rows in the sample. Nothing that had ever
been in the file was lost, but recovering this content now requires §2.4.)

## 1.5 Two defects found during the audit

### (a) `stableId` collides across example rows

[`ScenarioStableId.Compute`](src/Kronikol/Reports/ScenarioStableId.cs) hashes
`featureName::outlineId::scenarioDisplayName`. For a scenario outline whose rows share a display name,
every row hashes identically. Measured on the checked-in ReqNRoll example report: **6 scenarios, 4
distinct stableIds** — three rows of "Different muffin recipes should produce the expected batch" all
carry `e9bf0e2c34b5a8fd`, differing only in `exampleValues`, which is not in the hash.

The schema documents this field as "Deterministic cross-run identifier … **use this for matching the same
test across runs**". Cross-run matching is exactly what breaks: `diff` cannot tell row 1 from row 3, which
is the case where per-row matching matters most.

Blast radius is small and entirely external: no code in `src/` consumes it — the merge path matches on
something else — and it is written at three sites (JSON/XML/YAML) plus the XSD, mirrored by Kronikol4J.
Fix: fold `ExampleDisplayName` (or the ordered `ExampleValues`) into the hash when present. That changes
the ids of parameterised scenarios, so anyone storing them historically sees a discontinuity — hence a
decision, not a silent patch (§6.2).

### (b) A failing step's error message is captured, then dropped

`CollectedStep.ErrorMessage` is set when a step fails
([StepCollector.cs:121](src/Kronikol/Tracking/StepCollector.cs#L121)), but `ToScenarioStep`
([:402](src/Kronikol/Tracking/StepCollector.cs#L402)) does not map it and `ScenarioStep` has no field for
it. The scenario-level `errorMessage` survives, so you learn *that* the test failed; with nested
sub-steps you do not learn which one carried which message. Same family as the assertion message in
§1.4b, and the same fix shape — a model field, not just a serialiser change.

## 1.6 The ingest input format is richer than the report output format

`InteractionRecord` — the NDJSON contract for `kronikol ingest`, which is how non-.NET suites (the
Playwright/Node e2e project in question) get their data in — accepts `phase`, `metaType`,
`dependencyCategory`, `callerDependencyCategory`, `activityTraceId`, `activitySpanId`, `isUserAction`,
`capturedBy`, `durationMs`. `ToLog` maps all of them onto the record except `durationMs`.

**The round trip is lossy.** A capturer sends `activityTraceId`; Kronikol stores it, the diagram uses it,
and the JSON report drops it. Whatever else is decided, that asymmetry is worth closing on its own.

> **Bug to confirm:** `InteractionRecord.DurationMs` is documented on the interaction contract but `ToLog`
> ignores it (it is only consumed for *step* durations in Cucumber merging). Either map it onto the log or
> document it as step-only — silently accepting and dropping a field is the worst of the three options.

---

# Part 2 — Work item A: close the export gaps

Each item states **Today / Change / After** explicitly. All fields are additive; the schema describes
objects without `additionalProperties: false`, so existing consumers are unaffected. None of this changes
reports already on disk — only runs after the change, which is why Part 3 keeps a fallback path.

## 2.1 Interaction fields

- **Today:** `MapLogJson` emits 11 fields and drops the rest of the record (§1.4a).
- **Change:** add `metaType`, `dependencyCategory`, `callerDependencyCategory`, `phase`, `isUserAction`,
  `activityTraceId`, `activitySpanId`, `capturedBy`, and a derived `durationMs` (from the paired
  request/response timestamps; null when unpaired). Omit nulls/defaults so quiet reports do not grow.
  Mirror in `MapLogXml` ([:3266](src/Kronikol/Reports/ReportGenerator.cs#L3266)) and the YAML writer
  ([:3406](src/Kronikol/Reports/ReportGenerator.cs#L3406)), and extend both generated schemas.
- **Null policy is a decision, not a detail.** `MapLogJson` serialises an anonymous object with default
  options, so nulls are written (`"statusCode": null` appears throughout today). Adding eight mostly-null
  fields costs roughly 20 bytes each — ~190 KB on a 10.7 MB report, under 2%. Switching to
  `WhenWritingNull` would cost nothing but *removes existing keys* from the output, which is a
  format change consumers and Kronikol4J byte-parity both feel. Recommendation: keep writing nulls, take
  the 2%.
- **After:** an interaction in the JSON carries everything the diagram knows about it, and the OTel ids
  make report → trace → logs a real path.

## 2.2 Failure detail on steps and assertions

- **Today:** an assertion sub-step stores text + pass/fail; its failure message and file/line go only into
  PlantUML (§1.4b). A failing *step*'s message is collected into `CollectedStep.ErrorMessage` and then
  dropped in the model conversion (§1.5b).
- **Change:** one field set serving both. Add `FailureMessage`, `SourceFile`, `SourceLine` to
  `ScenarioStep`; map `CollectedStep.ErrorMessage` → `FailureMessage` in `ToScenarioStep`; thread
  `failureMessage`, `callerFilePath`, `callerLineNumber` from `Track.LogAssertion` through
  `StepCollector.AddAssertionSubStep` → `CollectedStep`; emit from both step mappers. Capture side
  included — the only item in Part 2 that is not purely a serialisation change.
- **After:** `{"text":"…","status":"Failed","message":"Expected 4173 but found 3902","file":"OverviewTests.cs","line":142}`
  — `kronikol query failures` can answer "why" without a diagram.

## 2.3 Step detail

- **Today:** `BuildFeaturesJsonModel(..., fullStepDetail: false)` for the standard JSON, so `Parameters`
  and `TextSegments` are dropped; `BypassReason`, `Comments`, `DocString` are dropped by both mappers.
- **Change:** (i) pass `fullStepDetail: true` for the standard JSON — a one-argument flip that turns on
  already-tested serialisation; (ii) add the four fields that neither mapper emits. Guard (i) behind
  `ReportConfigurationOptions.TestRunReportFullStepDetail` (default **true**) so anyone who needs the
  smaller file can opt out.
- **After:** a parameterised failure shows its inputs — data-table rows, example values, doc strings —
  in the file the agent reads.

## 2.4 Annotations

- **Today:** tabular row markers and injected fragments exist only as `PlantUml` on marker records, which
  no export emits.
- **Change:** add `RequestResponseLog.MarkerKind` (`Step | Assertion | Row | Custom`), set by each
  emitter — `StepCollector`, `Track`, `TabularInputs`, `InsertPlantUml` (default `Custom`). Classifying at
  the source beats regexing PlantUML at the sink. Then export a scenario-level
  `annotations: [{ index, kind, text }]` for `Row` and `Custom` only (`Step` and `Assertion` are already
  structured in `steps`).
- **After:** "which example row was in flight when this call happened" is answerable from the JSON, and
  user-authored diagram commentary stops being write-only.

## 2.5 Step ↔ interaction attribution

- **Today:** nothing connects `steps` to `httpInteractions`. The connection exists positionally in the
  log stream, via the step markers, and is consumed only by the diagram.
- **Change:** at export, walk the ordered stream and stamp each interaction with `stepPath` (`"2.3"`, an
  index path into `backgroundSteps`/`steps`). Markers are identifiable via `MarkerKind` (§2.4); match the
  *n*th step marker to the *n*th step in DFS order, with the marker's text as a verification fallback and
  a `DiagnosticKind` entry when they disagree.
- **Ordering is safe but not unconditional.** `RequestResponseLogger` is a `ConcurrentQueue`, so records
  enqueue in global FIFO order and a single test's records keep their relative order — parallel test
  execution interleaves *between* tests, which the `TestId` filter already separates. The exception is a
  test whose work spans background threads (a supported pattern — see the *Background Thread Correlation*
  wiki page), where a record can enqueue after the marker for the following step. Timestamp-based
  attribution would be the robust alternative, but steps currently time with `Stopwatch.GetTimestamp()`
  monotonic ticks and no wall-clock anchor, so it needs a `DateTimeOffset` captured at step start before
  it is usable. Recommendation: order-based matching with text verification now; add the wall-clock
  anchor alongside `startedAt`/`endedAt` if mismatches show up in practice.
- **After:** `query flow`, `query steps` with `[i12-i39]` ranges, step-scoped `failures`, and `compare`
  all work from JSON alone — the tool never parses PlantUML for an enriched report.

## 2.6 Run-level extras (optional, lower priority)

- **Today:** `componentRelationships`, `internalFlowSegments`, `ciMetadata` are mergeable-only.
- **Change:** emit `ciMetadata` unconditionally (tiny, and it identifies the run for `diff`), and a
  *summary* of internal flow rather than the full segment data — per scenario: span count, total time,
  the top N slowest spans. The full segment payload stays mergeable-only.
- **After:** "why is this slow" has a first answer without the mergeable file.

## 2.7 Cost of the schema surface

Every field added lands in **eight** places: JSON + XML + YAML writers and the schema generator, in .NET
and again in Kronikol4J, which mirrors all four
(`kronikol4j-report/.../data/ReportDataSerializer.java`, `ReportDataSchema.java`) and is maintained at
byte parity. Batch the additions into one pass rather than dribbling them out, and treat the Java port
as part of the same unit of work.

---

# Part 3 — Work item B: `kronikol query`

A subcommand of the existing dotnet global tool (`PackageId: Kronikol.Tool`, `ToolCommandName: kronikol`),
so consuming repos get it with `dotnet tool install -g Kronikol.Tool` and invoke `kronikol query …`.

## 3.1 Principles

1. **Progressive disclosure with stable addresses.** Every command prints pointers where a blob would go;
   every pointer is valid input to another command.
2. **Elide, never omit.** `content: <2.7 KB · b:4bdea521 ×28>` says something exists *and* how to get it.
   Silent omission is what sends an agent back to `cat`.
3. **Budget in bytes and announce truncation.** `--max-bytes` (default 6000) with a mandatory footer
   naming the exact re-run: `… 24 of 127 shown · --offset 24`.
4. **Aggregate before paginating.** 127 interactions become ~20 lines by collapsing identical repeats
   (`i12-i39  redis GET data-insights:v1:location* ×28  b:4bdea521`).
5. **Compact text by default, `--json` for piping** — JSON output costs ~2× the tokens.
6. **`--out FILE` costs zero context.** `wrote 64 KB → ./body-4bdea521.json` is six tokens; the agent
   greps the file instead of reading it.
7. **`--count` on every listing**, for yes/no questions.
8. **Never load the file** — `Utf8JsonReader`, single pass. Note the buffer must accommodate the largest
   single token: a diagram string can be megabytes, so the reader needs a growable buffer, not a fixed one.
9. **Resolve attachment paths.** `FileAttachment.RelativePath` is relative to the report directory (the
   HTML uses it directly as an `href`). Every command that prints an attachment prints an absolute path,
   so the agent can `Read` a screenshot without reconstructing anything.

## 3.2 Addressing

| Thing | Address | Why |
|---|---|---|
| scenario | `s3`, with the `stableId` prefix shown alongside | `s3` is two tokens, a GUID ~20; `stableId` is the cross-run key |
| step / assertion | `s3/st2`, `s3/st2/a1` | mirrors the steps tree and `stepPath` |
| interaction | `s3/i47` | ordinal within scenario, capture order |
| body | `b:4bdea521` (sha1-8 of content) | content-addressed → stable across runs and scenarios; dedup and diff for free |
| diagram / note | `s3/d0`, `s3/d0/n12` | for the divergence path (§3.4) |

Ordinals are deterministic per file (features by `DisplayName`, scenarios in file order). Cross-run work
uses `stableId` and `b:` hashes, both of which survive re-runs.

## 3.3 Commands

```
summary      R.json                       run header, per-feature pass/fail, slowest scenarios,
                                          diagnostics count, scenario table       [~2 KB]
scenarios    R.json [--result Failed] [--feature X] [--label smoke] [--grep t] [--slower-than 5s] [--count]
failures     R.json                       per failure: error, failing step in context, assertions
                                          (with §2.2 message + file:line), step-scoped interactions
steps        R.json s3                    step/assertion tree + parameters, with [i12-i39] ranges
assertions   R.json [s3] [--failed]       flat assertion list — expression, values, result, file:line
flow         R.json s3 [--step 2] [--service redis] [--errors-only]
                                          interleaved step bars / assertions / arrows, from JSON
services     R.json [s3]                  per service: calls, status mix, errors, bytes, p50/max.
                                          Answers ABSENCE — "did we ever hit bigquery?"
interactions R.json s3 [--service] [--status 5xx] [--method] [--grep] [--group] [--offset]
http         R.json s3/i47 [--headers] [--body] [--keys] [--path '$.d.x'] [--lines 20-60] [--out]
body         R.json b:4bdea521 [--path|--keys|--lines|--out]     + every address it occurs at
note         R.json s3/d0/n12 [--out]     the rendered note, for the divergence case (§3.4)
annotations  R.json s3                    §2.4 row markers and injected fragments
grep         R.json "4173" [--in bodies,headers,steps,assertions,uris,notes] [--values] [--count]
                                          returns ADDRESSES: `s3/i47  $.data.customers[2].total  … 4173 …`
compare      R.json s3 s7                 two scenarios in one run
diff         A.json B.json                two runs, matched on stableId
diagram      R.json s3/d0 --raw --out F   escape hatch; never printed to stdout
```

Why this set: `failures` and `grep --values` answer the two real questions ("why did it fail", "where did
this number come from" — remember the motivating run had **zero failures**). `services` is the only view
that answers negative questions. `compare` uses the passing neighbour as an oracle for the failing one.
`flow` replaces reading a diagram, at ~1–2 KB instead of 663 KB.

## 3.4 Handling divergence and old reports

Two conditions the tool must detect rather than assume:

- **Unenriched report** (generated before Part 2): no `stepPath`, no assertion `message`. The tool
  reconstructs step boundaries and assertion detail by parsing the diagram, and prints one header line
  saying so, so the agent knows the answer came from a slower, lossier path.
- **Notes diverge from captured content** (`FocusFields`, phase variants, formatting processors — §1.3).
  The tool already hashes bodies during its index pass, so it can reconcile note payloads against
  interaction contents per scenario and print `notes diverge from captured content — see query note`.

## 3.5 Implementation

- `src/Kronikol.Tool/QueryCommand.cs`, following `MergeCommand`/`IngestCommand`:
  `Run(args, out, error) → exit code`, same flag idiom.
- One `Utf8JsonReader` pass builds an index — per scenario and interaction: offsets, lengths, and the
  cheap scalars (service, method, uri, status, body hash + length). Payloads are seeked on demand.
- Body hashes computed during that pass; the hash *is* the dedup key and the `b:` address.
- Optional `.kronikol-query-index` cache keyed on size+mtime if the index pass proves slow in practice.
- Reads the standard JSON and the mergeable superset. XML/YAML out of scope — JSON is what agents get.

## 3.6 Testing

TDD per CLAUDE.md. Beyond per-command unit tests:

- **Golden output tests on every command.** An agent-facing format that drifts silently is worse than none.
- **Invariant test: no command emits a payload by default** — assert every command's output on the large
  fixture stays under its budget and contains no body text unless a `b:`/`--body` was named.
- **Budget/footer tests**: truncation always announces itself with a re-runnable `--offset`.
- **Large fixture** via the existing `LargeReportFixture` pattern in `tests/Kronikol.Tests.EndToEnd/`,
  including a >100 MB synthetic report to prove the streaming path.
- **Unenriched-report tests**: every command works, with the fallback header, on a pre-Part-2 file.

---

# Part 4 — Work item C: the `kronikol-test-debugging` skill

Shipped in this repo at `templates/skills/kronikol-test-debugging/` so consuming projects can copy it in,
and referenced from the wiki.

> [`AI-Integration-Prompt.md`](../Kronikol.wiki/AI-Integration-Prompt.md) currently instructs agents to
> "check the data in TestRunReport.json" — the instruction that causes this problem. Updating it is part
> of this work item.

## 4.1 Frontmatter

```yaml
---
name: kronikol-test-debugging
description: >
  Debug a test run from a Kronikol report (TestRunReport.json) — why a test failed, what a service
  actually returned, where a wrong value came from, what changed between runs, what was slow.
  Use whenever a Kronikol report exists (.logs/kronikol/, Reports/, TestResults/) and the question is
  about test behaviour. Never read TestRunReport.json directly: reports reach 10 MB / 2.7M tokens and a
  single embedded diagram can be 166k tokens.
---
```

## 4.2 Body

**A. The rule, first, with the number.** Never `Read`/`cat`/`Grep` `TestRunReport.json`, its `.html`, or a
diagram. A prohibition with the reason attached — reading the file is the obvious move, so the agent has
to know why the obvious move is wrong.

**B. The four-layer model** (§1.2), so the agent knows what is cheap *before* it asks. An agent that knows
`steps` is 0.4% of the file pulls the whole tree without hesitating; one that doesn't pages through it.

**C. The ladder.** `summary` → (`failures` | `scenarios`) → `steps s3` → `services s3` →
`interactions s3` → `http s3/i47 --keys` → `http s3/i47 --path …`. **Stop at the first rung that answers
the question.** Most questions end at rung three.

**D. Recipes keyed by what the user actually said** — the section that gets used:

| The user says | Sequence |
|---|---|
| "why did these tests fail?" | `failures` — usually sufficient alone |
| "the number on screen is wrong" | `grep "<value>" --values` → `http <addr> --path` → `compare` with a passing scenario |
| "did it even call X?" | `services` — absence is the answer, no payload needed |
| "what did X return?" | `interactions s3 --service X` → `http s3/iN --keys` → `--path` |
| "which example row broke?" | `steps s3` (parameters) + `annotations s3` |
| "what broke since yesterday?" | `diff old.json new.json` |
| "why is this slow?" | `summary` → `services --sort duration` → `flow s3 --slow` |
| "is this flaky?" | `diff` across runs, matched on `stableId` |
| "the report shows X but I can't find it" | `note` — the HTML renders notes, which are not byte-identical to `content` (§1.3) |
| "show me the flow" | `flow s3` — never the diagram |

**E. Budget discipline.** Read the `… N of M shown` footers and prefer *filtering harder* to paging. Use
`--count` for yes/no. Above ~10 KB use `--out FILE` then `Grep`; never print a payload you only need to
search.

**F. Traps**, each with its reason:

- Payloads come from `httpInteractions`, not diagram notes — but notes are a *different rendering*, not a
  copy, so if the user quotes something you cannot find, use `query note`.
- `b:` addresses are content hashes: same hash means byte-identical, so read it once.
- Ordinals (`s3/i47`) are per-file; use `stableId` and `b:` hashes across runs.
- A body ending `…truncated (N chars total)` was capped at capture time — the rest was never recorded.
- `attachments` are pointers; screenshots are worth `Read`-ing individually and are never inlined.
- If the tool prints the unenriched-report header, assertion detail came from diagram parsing and may be
  incomplete.

**G. Fallback** — `scripts/query.py` implementing `summary`, `steps`, `grep` and `http` against the same
addressing, so a machine without the tool degrades to "still never reads the raw file" rather than to
nothing.

**H. Answer style.** Cite addresses (`s3/i47`, `b:4bdea521`) so the user can verify any claim with one
command.

## 4.3 Layout

```
templates/skills/kronikol-test-debugging/
  SKILL.md                 # ~150 lines: rule, model, ladder, recipes, traps
  references/commands.md   # full flag reference, loaded on demand
  scripts/query.py         # no-tool fallback
```

---

# Part 5 — Documentation

CLAUDE.md requires the README, the changelog and — chiefly — the wiki at `../Kronikol.wiki` to be updated
whenever the public API or behaviour changes. This work changes the data-file contract, the CLI surface,
the options surface and the capture surface, so the load is real: **13 existing wiki pages, one new page,
the sidebar, both repo READMEs, and the Java port's own wiki.** The wiki is a separate git repository
(`github.com/lemonlion/Kronikol.wiki`), so it lands as its own commit alongside each phase.

| Page | What changes | Phase |
|---|---|---|
| **`AI-Integration-Prompt.md`** | **Highest priority.** It currently instructs agents to "check the data in `TestRunReport.json`" — the exact instruction that causes the context blow-up. Rewrite to route through `kronikol query` and the skill. | 3 |
| **NEW `Querying-Reports.md`** | Full `kronikol query` reference: the addressing scheme, budget flags, every command with examples, the unenriched-report fallback, and a *"Using it from an AI agent"* section covering the skill and its install. | 2–3 |
| `Generated-Reports.md` | The data-file reference, and the largest edit: new interaction fields, step failure detail, `parameters`/`textSegments` in the standard file, `annotations`, `stepPath`, revised `stableId` semantics, and the schema section. The `httpInteractions` marker-exclusion note added on 2026-08-22 already lives here. | 1, 4 |
| `Assertion-Tracking.md` | Assertion failure message and source location now reach the data file (§2.2). | 4 |
| `Step-Tracking.md` | Step failure message; step parameters now in the standard data file, not just the mergeable one. | 1, 4 |
| `Tabular-Attributes.md` | Row markers are exported as `annotations` (§2.4) instead of existing only in the diagram. | 4 |
| `Phase-Aware-Tracking.md` | `phase` is now in the data file. | 1 |
| `Event-Annotations.md` | `metaType` and `dependencyCategory` are now in the data file; the `DependencyCategory Reference` section gains a data-file column. | 1 |
| `Ingesting-External-Captures.md` | The NDJSON round trip becomes lossless (§1.6); `durationMs` semantics resolved either way; it is also the only page that documents `stableId`, so the collision fix (§1.5a) is documented here. | 1, 4 |
| `Diagnostics-and-Debugging.md` | Add `kronikol query` beside `DiagnosticMode` as a debugging entry point; also record the unpaired-request marker fix shipped 2026-08-22. | 2 |
| `Report-Configuration.md` | New `TestRunReportFullStepDetail` option. | 1 |
| `API-Reference.md` | New public surface: `ScenarioStep.FailureMessage`/`SourceFile`/`SourceLine`, `RequestResponseLog.MarkerKind`, and `RequestResponseLog.IsDiagramMarker` — which shipped on 2026-08-22 and is currently undocumented. | 1, 4 |
| `Merging-Parallel-Reports.md` | The gap between the standard and mergeable files narrows once full step detail is the default; the page's comparison needs restating. | 1 |
| `Internal-Flow-Tracking.md` | Only if §2.6 lands: the internal-flow summary in the standard data file. | 6 |
| `Home.md`, `_Sidebar.md` | Entry for the new page under **Features** (and a **Reference** link); the sidebar is hand-maintained. | 2 |
| `README.md`, `nuget-readme.md` | The CLI is described as "`kronikol merge` and `kronikol ingest`" in three places ([README.md:256](README.md), [nuget-readme.md:50](nuget-readme.md)); add `query`. | 2 |
| `Kronikol4J.wiki/Report-Data-Formats.md` | The Java port mirrors the data-format documentation as well as the serialisers. | with the port |
| `CHANGELOG.md` | One entry per phase, per CLAUDE.md. | every |

**Definition of done for each phase:** full suite green → patch bump across all packages → changelog entry
→ wiki pages above for that phase → tagged commit in both repositories.

---

# Part 6 — Sequencing

| Phase | Contents | Rationale |
|---|---|---|
| 1 | **A**: §2.1 interaction fields, §2.3 step detail (both are serialisation-only) | Immediate value to anyone reading the JSON today, no capture risk, and it settles the schema surface before the tool depends on it |
| 2 | **B**: `summary`, `steps`, `services`, `interactions`, `http`, `body` + budget machinery | The ladder end-to-end; answers most questions |
| 3 | **C**: skill + wiki updates (incl. `AI-Integration-Prompt.md`) | Without the skill the tool goes unused — agents `cat` by default |
| 4 | **A**: §2.2 assertion detail, §2.4 annotations, §2.5 `stepPath` | The capture-side changes, with the tool's tests already in place to catch regressions |
| 5 | **B**: `failures`, `flow`, `assertions`, `grep --values` | These get materially better once phase 4 lands |
| 6 | **B**: `compare`, `diff`; **A**: §2.6 run-level extras | Highest ceiling, most design surface; benefits from real usage first |

Per CLAUDE.md, each phase ends with the full suite green, a patch bump across **all** packages, a
changelog entry, the wiki pages listed in Part 5 for that phase, and a tagged commit. Kronikol4J parity
(§2.7) tracks phases 1 and 4, and carries its own wiki page with it.

## 6.1 Risks

- **Schema churn.** Two rounds of field additions (phases 1 and 4) mean two rounds of the eight-place
  update. Batching within each phase keeps it to two.
- **`stepPath` attribution drift.** Order-based matching is fragile if a capturer interleaves records
  across threads. Mitigation: verify against marker text, emit a `DiagnosticKind` entry on mismatch, and
  leave `stepPath` null rather than guess.
- **File growth.** §2.3's `fullStepDetail: true` makes the default report bigger. Mitigation: the opt-out
  option, and the fact that step detail is measured in kilobytes against megabytes of payload.
- **Attribution under background threads** — see §2.5; mitigated by text verification and a null
  `stepPath` rather than a wrong one.
- **Format drift between the tool and the report.** Golden tests on both sides; the tool must fail loudly
  on an unknown `mergeableFormatVersion`.

## 6.2 Open questions

1. Should `query` ship in `Kronikol.Tool` (assumed) or as a separate lighter package that consuming repos
   can install without the ingest/merge surface?
2. Skill distribution: `templates/` copy-in, or a `.claude-plugin` installed once per machine?
3. Are §2.1/§2.3 acceptable as additive changes in a patch release, or do they wait for a minor?
4. Is `TestRunReportFullStepDetail` defaulting to **true** the right call, given it grows every report?
5. Does the Java port follow in the same release, or track behind with parity tests marked pending?
6. **`stableId` (§1.5a): fix it, and when?** Folding example values into the hash changes the ids of every
   parameterised scenario — a discontinuity for anyone storing them. The alternative is leaving a
   documented cross-run key that cannot distinguish example rows, which `diff`/`compare` depend on.
   Recommendation: fix in the same release as §2.1, note it as behavioural in the changelog.
7. Should `query` accept a directory (or glob) and discover reports itself? A solution with several test
   projects has several `TestRunReport.json` files, and the agent should not have to guess which.
