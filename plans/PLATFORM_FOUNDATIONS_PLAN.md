# PLATFORM_FOUNDATIONS_PLAN.md — one renderer, N capturers

**Date:** 2026-09-14 · **.NET repo:** 3.8.0 (Debug DLL; drafted at 3.3.0) · **Kronikol4J:** 0.1.25-SNAPSHOT (pre-1.0)
· **Status: investigation complete, nothing implemented, NOT green-lit.**

**This is the definitive plan for taking Kronikol to four platforms** — .NET, Java, Node, Python — in
one repository. It is the foundations half: everything that must exist *before* the first new
platform is added, so that adding each one after it creates as little platform-specific code as
possible. Three follow-on plans — `JAVA_PLATFORM_PLAN.md`, `NODE_PLATFORM_PLAN.md`,
`PYTHON_PLATFORM_PLAN.md` — will each be an instance of §9's template.

> **§12 (2026-09-15)** records the sharing levers beyond the renderer: the taps as a shared end-to-end
> artifact (F10), baggage as their companion, HAR and CTRF importers, more raw facts under one rule,
> generated option types, processors as a rule list (with the one Java regression named), and a single
> in-process rendering path on .NET. Its §12.7 amends F1–F9; its §12.5 is the fidelity accounting.

**Where it contradicts an existing plan, this one wins.** §5 lists every supersession explicitly.
`MONOREPO_MIGRATION_PLAN.md` is adopted whole as F0. `KRONIKOL4J_PORTABILITY_PLAN.md`,
`QUERY_PORTABILITY_PLAN.md`, `QUERY_FALLBACK_PLAN.md` and `NEXT_LANGUAGE_PLAN.md` are absorbed
into milestones here. `JAVA_PORT_PLAN.md` and `NODE_PORT_PLAN.md` keep their per-language design
work but lose their rendering halves and their "full feature parity" end state.

---

## 0. The architecture in one sentence

> **Every platform is a capturer that writes two NDJSON streams and an options file. One shared
> renderer, built once from .NET, turns them into every report. One shared query engine reads the
> result. Nothing else is written per platform.**

```
                 ┌────────────────────────────────────────────────────────────┐
                 │  PER PLATFORM (dotnet / java / js / py)                     │
                 │                                                            │
   test run ───▶ │  test-framework adapter ──▶ tests.ndjson    (start/step/   │
                 │                                              assertion/     │
                 │  capture adapters ─────────▶ interactions.ndjson  end)      │
                 │    http · sql · (tail via OTel, no bodies)                  │
                 │                                                            │
                 │  options ──────────────────▶ kronikol.render.json           │
                 └───────────────────────────────┬────────────────────────────┘
                                                 │  the contract  (§4, F2)
                                                 ▼
                 ┌────────────────────────────────────────────────────────────┐
                 │  SHARED — built once from .NET                              │
                 │                                                            │
                 │  kronikol-render   (wasm, or NativeAOT if E1 fails)        │
                 │    = today's `kronikol ingest` pipeline                    │
                 │    ▶ TestRunReport.html/.json/.yml/.xml · Failures.md      │
                 │      Specifications.* · ComponentDiagram.html · CLAUDE.md  │
                 │                                                            │
                 │  kronikol-query    (same artifact, second entry point)     │
                 │  the agent skill · the parity corpus · the conformance kit │
                 └────────────────────────────────────────────────────────────┘
```

**.NET is platform #1, not the exception.** Today it is the only platform that also *contains* the
renderer. The first and most important foundation (F1) is making .NET itself work through the
boundary — so the boundary is proven complete by the platform with the most features, before any
other platform has to trust it.

---

## 1. What "as little platform-specific stuff as possible" means, concretely

The definitive split. If something is in the left column it is written N times; if it is in the
right column it is written once. **Every design decision in this plan is a decision to move
something from left to right.**

| Per platform — written N times | Shared — written once |
|---|---|
| Ambient test-id context (the thing that stamps `testId` on every capture) | Report rendering: HTML, JSON/YAML/XML, PlantUML source, `Failures.md`, `Specifications.*`, component diagram, CI summary |
| NDJSON writers for the two streams | `kronikol query`, `merge`, `export`, `ctrf`, `init-agents` |
| HTTP capture adapter (with bodies) | SQL classification, service-name resolution, scenario-title resolution, string casing, safe serialisation — **every pure function** (F3) |
| SQL capture adapter (with bodies / statement text) | Redaction-for-display (redaction-for-security stays per platform — §4.4) |
| OTel bridge for the tail (one, no bodies) | The contract, its schemas, its validator, its version |
| One test-framework adapter per framework (~100 lines each) | The parity corpus and conformance kit |
| The run-end hook that invokes the shared renderer (~50-line host shim) | The agent skill (`kronikol-test-debugging`) |
| Packaging, per-ecosystem | CI shape (`parity.yml`), release mechanics for the shared artifact |
| Structured stack frames, the allow-listed environment and the failing line's snippet, gathered at capture (§12.2, ~30 lines each) | Stack-trace interpretation, CI detection, the note rule list (mid/post processors), the HAR and CTRF importers (§12) |
| Stamping identity as headers + baggage at egress (§12.1 L2) | The end-to-end tap artifact, F10: HTTP, Redis, Mongo, Postgres, MySQL wire capture with bodies (§12.1 L1) |
| *Generated, not written:* the record types, the options object with its defaults, the validator (§12.1 L7) | The schemas they are generated from, and the options reference |

Measured against Kronikol4J today, the left column is ~5,600 lines (`NEXT_LANGUAGE_PLAN.md` §2.1),
and F3 shrinks it further by moving ~1,000 lines of duplicated pure functions to the right.

**The principle that decides ambiguous cases: raw facts cross the boundary; interpretation stays
inside.** A capturer records *what happened* — method, URI, raw SQL, raw body, timestamps, test id.
It does not decide what a SQL statement's table is, what casing a step name should have, or what a
service is called. The moment a capturer interprets, that interpretation has to agree across N
languages, and agreement is exactly the cost being eliminated.

---

## 2. Where we are today — the measured gap inventory

Every item was verified against the code on 2026-09-14, not inferred.

| # | Gap | Evidence | Closed by |
|---|---|---|---|
| **G1** | **.NET cannot write tests NDJSON.** `TestRunRecord` is read by `FeatureSynthesizer` and never written by anything — no `NdjsonTestRunWriter` exists | `grep -rl TestRunRecord src/` → 8 files, all readers | F1 |
| **G2** | **.NET writes interaction NDJSON, but only for OTLP.** `NdjsonInteractionWriter` is an `IRequestResponseSink` and composes via `CompositeRequestResponseSink`, but the only caller is `OtlpTapOptions` — there is no capture-mode option | `grep -rl NdjsonInteractionWriter src/` → the class + `OtlpTapOptions.cs` | F1 |
| **G3** | **No round-trip parity test exists.** Nothing asserts that in-process report == `ingest(ndjson)` report | `tests/Kronikol.Tests/Ingestion/` has 16 tests; none compares the two paths | F1 |
| **G4** | **`ingest` cannot take internal-flow spans.** `IngestPipeline.DefaultOptions()` sets `InternalFlowTracking = false` — "there are no in-process spans to show" | `IngestPipeline.cs:284` | F5 |
| **G5** | **The options channel is 27 CLI flags against 95 options.** `kronikol ingest` exposes 27 `--flags`; `ReportConfigurationOptions` has 95 settable properties | `grep -c '{ get; set; }'` = 95; `case "--…"` = 27 | F4 |
| **G6** | **Pure functions are duplicated per language.** Kronikol4J's core reimplements `UnifiedSqlClassifier` (297 lines), `ServiceNameResolver`, `ScenarioTitleResolver`, `StringCasing`, `TrackingSafeSerializer`, `TestInfoResolver`, and byte-diffs each against .NET | `java/kronikol4j-core/.../{sql,naming,serialization}/` | F3 |
| **G7** | **The parity corpus is render-shaped.** 94 of 109 fixtures are rendered output (`.puml`, `report-*.html`, data files); 15 are capture-side (`*-interactions.txt`, classifications) | `ls parity-harness/dotnet-capture/fixtures/` classified | F8 |
| **G8** | **Java has no NDJSON path at all**, and every renderer it has is invoked from one class — `ReportFinalizer.java`: `HtmlReportGenerator` (l.95), `ComponentDiagramReportGenerator` (l.103), `DiagnosticReportGenerator` (l.155), `CiSummaryGenerator` (l.179) | `grep -rli ndjson java/` → nothing; `ReportFinalizer` call sites enumerated | Java platform plan, using F6's shim — four calls become one |
| **G9** | **The shared renderer does not exist as an artifact.** No WASI build has been attempted (A9 in `KRONIKOL4J_PORTABILITY_PLAN.md`) | — | F6 |
| **G10** | **Not one repository.** `MONOREPO_MIGRATION_PLAN.md` is unexecuted | `C:\Code\Kronikol` and `C:\Code\Kronikol4J` are siblings | F0 |
| **G12** | **`suite` is taken from the RENDERING process's entry-assembly name, and `stableId` is computed from suite** — a shared renderer would stamp its own name on every platform's report and shift every stableId. **Measured** in E1: reference `suite: null / 60d7d218…`, scratch build `suite: "Native" / 785705616…`; pinned via `ReportConfigurationOptions.SuiteName` (line 41). Must cross the boundary | E1 diff; `ReportConfigurationOptions.cs:41` | F2 (header record) / F4 |
| **G11** | **The contract records which renderer wrote a report, not which capturer.** `KronikolVersion` is stamped; the capturing platform and its version are not a field | `InteractionRecord` fields inspected — no producer field | F2 |

G1–G3 together are the reason F1 comes first: **.NET cannot currently prove its own boundary is
complete.** Everything downstream inherits whatever that boundary is missing.

---

## 3. The foundations — F0 to F9

Each milestone states what it is, why it is a foundation rather than a feature, when it is done,
and the test that says so. Order matters and §6 gives it.

### F0 — One repository

**What.** Execute `MONOREPO_MIGRATION_PLAN.md` phases 1–5 as written, with two additions: a `py/`
subtree reserved in §0's layout, and `parity/` gaining the structure §F8 defines rather than a flat
`fixtures/`.

**Why a foundation.** `parity.yml` — regenerate goldens from live .NET and fail the PR that drifts —
is impossible while the repos are split. Every later milestone's "done when" is a CI job in this
layout.

**Done when.** That plan's §10 rows 1–5 are green, plus `py/` exists with a README saying what will
land there and `parity/` has the F8 shape.

### F1 — .NET dogfoods its own boundary *(the gate for everything)*

**What.** Three pieces:

1. `NdjsonTestRunWriter` — the missing half. Emits `start`/`step`/`assertion`/`attachment`/`end`
   records from the in-process run, the mirror of what `FeatureSynthesizer` reads.
2. `ReportConfigurationOptions.CaptureMode = InProcess | Ndjson | Both`. `Ndjson` writes the two
   streams and the options file (F4) and generates nothing; `Both` does both, via
   `CompositeRequestResponseSink`, which already exists for exactly this.
3. **The round-trip parity test.** For every example project and every existing report fixture:
   run in-process → `Reports/`; run with `CaptureMode.Ndjson` → `kronikol ingest` → `Reports-ingested/`;
   **diff the directories byte-for-byte** after the canonicaliser (F8). Every difference is a hole in
   the contract, found on .NET, before any other platform exists.

**Why a foundation.** This single test is the contract's completeness proof and the renderer's
text-in/text-out proof at once. A wasm of the ingest pipeline (F6) is only worth building if the
ingest pipeline can reproduce the in-process report; today nothing says it can, and G4 says it
cannot for at least one feature.

**Done when.** The round-trip test passes across the whole corpus, and it runs in `dotnet-ci.yml`
on every PR. **Expect it to be red for a while** — that is its job. Each red is a G-row for F5.

### F2 — The contract, versioned and language-neutral

**What.** `InteractionRecord` and `TestRunRecord` become a published contract rather than two C#
records with good doc comments:

- `parity/contract/interaction.schema.json`, `tests.schema.json`, `render-options.schema.json`
  (the last from F4) — generated from the C# records so they cannot drift, checked in so every
  platform's tests can validate against them without .NET.
- `parity/contract/CONTRACT.md` — the prose: required fields, pairing rules (`requestResponseId`),
  correlation (`traceId` vs `activityTraceId`), the redaction split (§4.4), the capped-body marker,
  the tail-via-OTel degraded form and how a capturer marks it.
- A header record on each stream: `{"kind":"header","captureFormatVersion":1,"producer":"kronikol4j
  0.2.0","platform":"java"}` — closes G11, and gives the renderer a way to refuse or adapt a stream
  from the future.
- A validator: `kronikol validate <dir>` — the first thing any new platform's tests call.

**Why a foundation.** `NEXT_LANGUAGE_PLAN.md` §2 established the contract is small (five required
interaction fields). Small is what makes it freezable. F1 makes it complete; F2 makes it something a
Python developer can implement from a document.

**Done when.** The schemas are generated by a test that fails if the C# records change without
regenerating; `kronikol validate` accepts F1's output and rejects a deliberately broken stream; the
Java capture goldens (F8) validate.

### F3 — Raw facts in, interpretation inside

**What.** Move every pure function that Kronikol4J's core currently duplicates to the renderer side
of the boundary, and make the contract carry the raw input instead:

| Today, per platform | After F3, the capturer sends | The renderer does |
|---|---|---|
| `UnifiedSqlClassifier` (297 lines in Java) | raw statement text + `dialect` hint | classification, table, keyword, arrow label |
| `ServiceNameResolver` | the URI and any explicit name it was given | resolution and defaulting |
| `ScenarioTitleResolver`, `StringCasing` | the test's raw name and the framework's own display name | title and casing rules |
| `TrackingSafeSerializer` | bytes, with `contentType` | decoding, pretty-printing, capping display |

**Why a foundation.** G6 shows these are ~1,000 lines per language *and* a parity surface each — the
Java port byte-diffs its classifier against .NET's. After F3 the classifier has one implementation
and zero parity tests, because there is nothing to compare. This is the largest single reduction of
per-platform code available, and it is a contract change, so it has to happen before the contract
freezes.

**Done when.** The .NET in-process path routes through the same functions the ingest path uses (the
round-trip test in F1 proves it); the Java classifier and naming classes are deletable — not yet
deleted; that is the Java platform plan — with the capture goldens still passing.

**One deliberate exception.** *Security* redaction (§4.4) stays per platform. A secret that reaches
the NDJSON file has already leaked.

### F4 — The options channel

**What.** `kronikol.render.json` beside the two streams: the full `ReportConfigurationOptions`
surface as JSON, schema-generated (F2), read by the renderer. The 27 CLI flags on `kronikol ingest`
stay as overrides for humans; the file is what platforms write.

**Why a foundation.** 95 options cannot be CLI flags and cannot be re-declared in four languages.
Each platform exposes its idiomatic options object (a Java builder, a TS interface, a Python
dataclass) that serialises to this file. The renderer owns the defaults; a platform that writes
`{}` gets exactly .NET's defaults, which is the right behaviour for a new port.

**Done when.** `CaptureMode.Ndjson` on .NET writes the file from its options object; the round-trip
test passes with a non-default options set (toggle defaults, focus fields, excluded headers, note
format); the schema is in `parity/contract/`.

### F5 — Close what the round-trip test finds

**What.** Whatever F1 turns red. Known before it runs: internal-flow spans (G4) need an NDJSON form —
most likely a third stream, `spans.ndjson`, since spans are neither interactions nor test events.
Probable, unverified: tabular-authoring inputs/outputs, component-registry "registered but never
invoked" diagnostics, assertion-tracking records, `Track.That` markers.

**Why a foundation.** Each of these is a feature that exists on .NET and that the shared renderer
must be able to render *from the contract* or the other platforms can never have it. Discovering the
list on .NET, with the round-trip test, is cheaper than discovering it one platform at a time.

**Done when.** The round-trip test is green across the whole corpus with no exclusions. Exclusions
are allowed only with a G-row naming the feature and the platform plan that owns it.

### F6 — The shared renderer artifact

**What.** `KRONIKOL4J_PORTABILITY_PLAN.md` E1 and E2, run against the *ingest* pipeline as it stands
after F5:

- **E1.** `dotnet publish` `Kronikol.Reports` + `Ingestion` to a WASI Preview 1 module,
  `kronikol-render.wasm`, entry `render(dir)`. Run F8's corpus through it. Byte-identical to
  `kronikol ingest` or it is dead.
- **E2.** Time it under Chicory (Java), `node:wasi` (Node), `wasmtime-py` (Python). Seconds is fine;
  minutes is not.
- **The fallback if E1 fails:** `QUERY_PORTABILITY_PLAN.md`'s NativeAOT route — still one
  implementation, per-RID instead of one artifact, with the JVM-portability caveat that plan's §3.4
  records. The per-platform shim is the same size either way.
- **The host-shim specification**, one per platform, ~50 lines: locate the artifact, preopen the
  captures directory, call `render`, surface stderr. Written once as a spec in `parity/contract/`,
  implemented per platform plan.
- **§8.1 of the Kronikol4J plan as a test:** the module imports nothing outside P1's floor (files,
  stdout, args). A change that reaches for a socket fails CI rather than stranding a host.

**Why a foundation.** This is what turns "one renderer" from a principle into an artifact. Without
it the alternative is the status quo: 14,305 lines of rendering per platform, and the parity ledger
that comes with them.

**Done when.** E1 byte-identical; E2 within budget on all three hosts; the artifact is built and
attached by CI on every `dotnet-v*` tag; the P1-floor test is green.

### F7 — The shared query engine

**What.** `kronikol query` becomes a second entry point on F6's artifact — `query(argv)` — so a Java
or Python user runs the same engine with the same verbs. `QUERY_FALLBACK_PLAN.md`'s `query.cs`
still ships beside every report for .NET users with a warm cache; F7 is for the users who have no
.NET at all.

**Why a foundation.** The agent skill documents `kronikol query`. If the query engine is
per-platform, the skill is per-platform, and the LLM-facing story fragments.

**Done when.** `kronikol query summary` through the wasm entry point is byte-identical to the .NET
tool across the query conformance corpus (that plan's M1).

### F8 — The parity corpus, reshaped around the contract

**What.** `parity/` becomes:

```
parity/
├── contract/          schemas, CONTRACT.md, host-shim spec, validator tests   (F2, F4, F6)
├── corpus/            <scenario>/interactions.ndjson + tests.ndjson + kronikol.render.json
│                      + expected/ (the renderer's output, canonicalised)
├── capture/           <adapter>/<service>/ — what a capturer must emit against a live service
│                      (today's 15 capture-side fixtures, promoted)
├── dotnet-capture/    the .NET harness, now producing corpus/ and capture/, not .puml
└── canonicalize/      ONE canonicaliser (NODE_PORT_PLAN §6.6's design), used at capture and assert
```

The 94 render-side fixtures **stop being cross-language**. They become the renderer's own
regression suite, under `dotnet/tests/`, because after F6 there is one renderer and nothing to
compare it to. The 15 capture-side fixtures become the thing every platform is measured against.

**Why a foundation.** This is the corpus that defines "done" for every platform plan: a Python port
is finished when `parity/capture/` and `parity/corpus/` pass through its capturer and the shared
renderer. Without it, "done" is a judgement call, and the Kronikol4J ledger records how those go.

**Done when.** `parity.yml` regenerates `corpus/expected/` from live .NET on any change to
`dotnet/**` or `parity/**`, and runs every platform's capturer against `capture/`; the Java capture
goldens are green in the new shape.

### F9 — The platform template and conformance kit

**What.** The thing that makes platforms two, three and four cheap:

- **The adapter SPI, as a spec.** Six operations a test-framework adapter must perform (the
  `NODE_PORT_PLAN.md` §5 six-row SPI, generalised): run start, test start/end, step start/end,
  assertion, attachment, run end → invoke shim. ~100 lines per framework, and the spec says which
  100.
- **The capture-adapter conformance test**, parameterised by live service: "against this
  Testcontainers Postgres, produce these lines" — `parity/capture/` as a runnable kit rather than
  files to diff by hand.
- **A skeleton generator** — `kronikol new-platform <lang>` — that writes the `py/` (or next) tree:
  the two writers, the shim, one adapter stub, the conformance test wired to `parity/`, CI file,
  CLAUDE.md. Opinionated, so the fourth platform looks like the third.
- **CI shape**: `<lang>-ci.yml` path-filtered on `<lang>/**` + `parity/**`, `<lang>-v*` tags, and
  membership in `parity.yml`.

**Why a foundation.** The user's requirement is that adding a platform creates as little
platform-specific work as possible. F0–F8 make that *possible*; F9 makes it *the path of least
resistance*.

**Done when.** `kronikol new-platform py` produces a tree whose conformance tests run (and fail,
because the adapters are stubs) in CI on the first commit.

---

## 4. Contract questions the foundations must settle

These are the design decisions inside F2/F4 that are not obvious and would otherwise be settled
differently by each platform.

### 4.1 Three version numbers cross the boundary

`captureFormatVersion` (the contract), the renderer's version (stamped as `kronikolVersion` today),
and the producing platform's version (G11, new). The report records all three. The renderer accepts
any `captureFormatVersion` ≤ its own and refuses anything greater with a message naming the upgrade.

### 4.2 Streams are append-only and per-process

A sharded or parallel run produces many files. The renderer takes a *directory* and reads every
`*.ndjson` in it — so **cross-process merge is concatenation**, and `NODE_PORT_PLAN.md` §5's
fragment-per-file model is the contract's native shape, not a Node workaround. `kronikol merge` and
the mergeable-format JSON stay for .NET in-process users; they are not part of the contract.

### 4.3 Attribution is the capturer's job, with a fallback

`testId` on every interaction is the first-class mechanism and every platform must implement the
ambient context that stamps it. `IngestRequest.AttributeByTestWindow` (timestamp-window attribution)
exists as a fallback for capturers that cannot — proxies, TCP taps — and stays available, marked in
the report as inferred.

### 4.4 Two redactions, deliberately

**Security redaction happens at capture, per platform** — a secret in the NDJSON file has leaked.
**Display redaction happens in the renderer** — `RequestResponseLogger.Redaction` already applies on
ingest. A capturer that redacts marks the record (`x-kronikol-redacted` pseudo-header, the pattern
`captured-by` already uses) so the renderer neither double-masks nor reports a masked value as the
system's real output. `NODE_PORT_PLAN.md` §3.13's "parity mode vs redact-at-capture mode" is the same
distinction and is adopted.

### 4.5 Bodies are bytes with a content type

The contract carries `content` as the capturer saw it plus `contentType`; decoding, pretty-printing
and the capped-body marker are the renderer's (F3). A capturer that must cap at capture (a streaming
response) writes the cap marker the renderer already understands.

### 4.6 The OTel tail is marked as such

An interaction sourced from an OTel span rather than a body-capturing adapter carries
`captured-by: span` (already in the model) and no `content`. The renderer renders it as it renders
capped records today — present in the diagram, thin in the note. `NEXT_LANGUAGE_PLAN.md` §1 is the
reason there is no attempt to do better.

---

## 5. What this plan supersedes, explicitly

| Plan | Kept | Superseded |
|---|---|---|
| `MONOREPO_MIGRATION_PLAN.md` | Everything — adopted as F0 | Adds `py/`; `parity/` takes F8's shape instead of flat `fixtures/` |
| `KRONIKOL4J_PORTABILITY_PLAN.md` | §1–§4 measurements; E1/E2; §8.1 syscall floor | E1 moves to F6 and runs against the *post-F5* ingest pipeline, not `Kronikol.Reports` as it stands; §6's "run E1 before 1.0" becomes "run F1–F6 before 1.0" |
| `QUERY_PORTABILITY_PLAN.md` | The AOT readiness measurement; NativeAOT as F6's fallback; the query conformance corpus (its M1) | The per-platform binary as the *primary* route; the Java-CLI-only carve-out in §3.4 |
| `QUERY_FALLBACK_PLAN.md` | All of it — `query.cs` still ships beside .NET reports | Nothing; it is orthogonal |
| `NEXT_LANGUAGE_PLAN.md` | §1 (OTel cannot carry bodies), §2.1 (the cost model), §3 (the ranking: Python after Node) | §2's "scope to core+HTTP+SQL" becomes §1's left column — a definition, not a recommendation |
| `JAVA_PORT_PLAN.md` | The capture-side decomposition and Appendix A source map; the deep dives on proxies/weaving/context | **"Full feature parity" as the end state.** The rendering half (`-report`, `-diagram`, 14,305 lines) is replaced by F6's shim at `ReportFinalizer.java`'s four generator calls (l.95, 103, 155, 179). Core's duplicated pure functions go with F3. Scope = §1's left column |
| `NODE_PORT_PLAN.md` | Seams A/B; §3.11 HTTP hooks; §3.12 driver wrapping; §3.10 dual-package singletons; §5's six-row adapter SPI (promoted to F9's spec); §6.1 deterministic clock (needed at capture) | **"Full feature parity"**; `@kronikol/report`, `@kronikol/diagram` and §4's HTML-assembly port (never built — F6); most of §6.3–§6.7 (PlantUML/HTML/serialiser parity is the renderer's own regression suite now, not Node's); §5's merger (§4.2 — concatenation) |

**What this does to the Java port specifically.** Kronikol4J is pre-1.0. Its 1.0 should be: F6's
shim in `ReportFinalizer`, `-report` and `-diagram` deleted (after F6 has baked one .NET release —
`KRONIKOL4J_PORTABILITY_PLAN.md` §6's mitigation), core's six duplicated classes deleted (after F3),
and the 30 tail modules marked experimental behind the OTel bridge. That is the Java platform plan's
whole content, and it is subtraction.

---

## 6. Sequencing and gates

```
F0 monorepo ─────────────────────────────────────────────────────────────┐
                                                                          │
F1 .NET round-trip ──┬─▶ F5 close the gaps ──┬─▶ F6 renderer artifact ──┼─▶ F9 template
                     │                       │                           │
F2 contract ─────────┤                       └─▶ F7 query entry point    │
                     │                                                    │
F3 raw facts in ─────┤   (F3 and F4 change the contract; they must       │
                     │    land before F2 freezes captureFormatVersion 1)  │
F4 options channel ──┘                                                    │
                                                                          │
F8 corpus reshaped ───────────────────────────────────────────────────────┘
        (starts after F0; its "expected/" is regenerated as F1–F5 land)
```

**Gates, in order:**

| Gate | Passes when | If it fails |
|---|---|---|
| **F1** | Round-trip test exists and runs (red is fine) | Nothing else is worth starting |
| **F5** | Round-trip green, no exclusions | The contract is incomplete; no platform can be complete either |
| **F6 / E1** | WASI build renders the corpus byte-identically | Fall back to NativeAOT; the plan survives, the "one artifact" property does not |
| **F6 / E2** | Seconds, not minutes, on all three hosts | Chicory AOT mode; failing that, NativeAOT for Java only |
| **F8** | `parity.yml` green with Java's capture goldens in the new shape | The corpus is wrong, not Java |

**Before Kronikol4J 1.0:** F0–F8. Not F9 — the template is proven by the *second* new platform.

**Before the first new platform plan is written:** F9.

---

## 7. Assumption ledger

| # | Claim | Status |
|---|---|---|
| P1 | .NET writes interaction NDJSON but not tests NDJSON | **measured** — writer class exists, no `TestRunRecord` writer |
| P2 | No round-trip test exists | **measured** — `tests/Kronikol.Tests/Ingestion/` inspected |
| P3 | `ingest` disables internal flow | **measured** — `IngestPipeline.cs:284` |
| P4 | 95 options vs 27 flags | **measured** |
| P5 | Six pure-function classes duplicated in Java core | **measured** |
| P6 | 94/109 fixtures are render-side | **measured** by filename classification; a handful may be misfiled |
| P7 | `ReportFinalizer.java` is Java's only renderer seam | **measured** — four generator calls (HTML l.95, component diagram l.103, diagnostic l.155, CI summary l.179), all in that one class. One shim replaces all four. |
| P8 | The round-trip test will find gaps beyond G4 | **expected, not measured** — F5 exists to find out |
| P9 | The pure functions in F3 have no per-platform inputs the renderer cannot receive | **assumed.** `ServiceNameResolver` may consult platform config (e.g. a Spring bean name). If so the *resolved* name crosses the boundary for that case and the renderer defaults the rest. Check in F3. |
| P10 | WASI Preview 1 can run the ingest pipeline | **NOT VERIFIED** — A9 in the Kronikol4J plan. F6 is the gate. |
| P11 | Concatenation is a sufficient merge (§4.2) | **reasoned** from the ingest path reading directories today; ordering across files is by timestamp and needs the deterministic clock at capture |
| P12 | ~50 lines per host shim | **estimated** from the wasmtime-py and `node:wasi` probes; Chicory unmeasured |
| P13 | A skeleton generator is worth building (F9) | **assumed** — worth it at platform three; the Python plan decides |

**P10 is the gate. P8 is the plan's honest uncertainty: F1–F5 are sized as "close what the test
finds", and nobody knows how long that list is until it runs.**

---

## 8. Decisions needed before green-lighting

1. **`captureFormatVersion 1` freezes after F3 and F4, not before.** Agreeing that the contract is
   not frozen until the raw-facts move and the options file are in — otherwise both become version 2
   and every platform pays a migration.
2. **The renderer's name and home.** `kronikol-render` as an entry point on one artifact with
   `query`, or two artifacts. One is simpler to version; two lets a platform ship query without
   render. Recommendation: one.
3. **Java 1.0 waits for F6.** §5 says Kronikol4J 1.0 = subtraction. That means 1.0 is gated on the
   shared renderer existing. The alternative — 1.0 on the current architecture, then a 2.0 that
   deletes the renderer — is the "replace a shipped renderer" cost the Kronikol4J plan argues
   against.
4. **Whether render-side fixtures stay in `parity/` at all.** F8 moves them to `dotnet/tests/`.
   Keeping a copy in `parity/` as "what the renderer promises" is defensible but doubles the
   re-bless cost.
5. **The tail's OTel bridge — shared or per platform?** OTLP is a wire format; an OTLP→NDJSON bridge
   could be *one more entry point on the shared artifact* (`kronikol export` already goes the other
   way). That would move the tail from the left column to the right. Not costed here; worth a spike
   in F5.

---

## 9. The template every platform plan will follow

Each of `JAVA_PLATFORM_PLAN.md`, `NODE_PLATFORM_PLAN.md`, `PYTHON_PLATFORM_PLAN.md` is this
outline, filled in. Nothing in it is a rendering decision.

1. **Ambient context** — how `testId` reaches every capture on this runtime (ALS, ThreadLocal +
   propagation, contextvars).
2. **The two writers** — NDJSON, per §4.2's per-process file rule, with the header record.
3. **HTTP adapter** — the runtime's seam(s), body capture without consumption, identity headers.
4. **SQL adapter** — the runtime's driver seam, statement + parameters, bodies of results.
5. **OTel bridge** — span → interaction, marked `captured-by: span`.
6. **Test-framework adapters** — one per framework, against F9's SPI, ~100 lines each. The plan
   lists which frameworks and in what order.
7. **The host shim** — F6's spec, implemented.
8. **Options** — the idiomatic options object that serialises to `kronikol.render.json`.
9. **Conformance** — `parity/capture/` and `parity/corpus/` green through this platform.
10. **Packaging and release** — the ecosystem's conventions, `<lang>-v*` tags.
11. **What is deliberately not built** — every left-column item not in scope, named.

For Java, items 1–5 exist and item 7 replaces 14,305 lines. For Node, items 1–6 are designed in
`NODE_PORT_PLAN.md` and items 7–11 are new. For Python, everything is new and F9's generator writes
the skeleton.

---

## 10. What this plan does not do

- **It does not build any platform.** Foundations only. The first platform plan starts after F9.
- **It does not decide wasm vs NativeAOT.** F6/E1 decides, with evidence.
- **It does not touch the .NET user-facing API.** `CaptureMode` is additive; `InProcess` stays the
  default.
- **It does not delete anything in Kronikol4J.** It says what the Java platform plan will delete,
  and after which gate.
- **It does not promise a fourth platform.** It makes the fourth cost roughly what the third did.

---

## 11. Verification log — 2026-09-14 (P10, P8, Decision 5), all measured

**Decision 5 — VERIFIED, and already built.** `src/Kronikol.Extensions.Otlp/` contains `OtlpTraceReader`
(hand-written protobuf + JSON; the csproj has no package references) and `SpanToInteractionMapper`, which reads
*standard* semconv (`db.query.text`←`db.statement`, `http.request.method`←`http.method`, `url.full`←`http.url`,
`server.address`←`net.peer.name`), Db+Http on by default, documented as usable "offline … without ever
starting a listener". Export already carries bodies opt-in (`kronikol.request.body`). The shared tail bridge is
a relocation into the F6 artifact, not a spike. Per-platform residue: write the SDK's spans to an OTLP-JSON file.

**P8 — VERIFIED with a list.** `TestRunRecord` (34 fields) has no field for, and `FeatureSynthesizer` never
sets: `Scenario.FailureCause`, `Scenario.Attempt`, `SourceFile`/`SourceLine` at Scenario, Step and Feature
level, `Scenario.ExampleDisplayName`, `ScenarioStep.FailureMessage` (a failed step "becomes a step comment").
Non-model: `CiMetadata.Detect()` reads env at *render* time (`CiMetadata.cs:21`); `RunEnvironment` is
`Unrecorded` on generic ingest (`IngestPipeline.cs:396` records the wrong-runtime bug this caused for the
Cucumber path); `ReportDiagnostics.Analyse` reads `InternalFlowSpanStore` (`:47`) and
`TrackingComponentRegistry` (`:63`) statically; plus **G12 suite**. F5's list is **at least 11 items** before
the round-trip test runs. Side finding: real `kronikol ingest` wrote 0-byte `Specifications.html`/`.yml`
(verified 2026-09-22: the blank-on-failed-run rule, not an ingest defect; `INGEST_FEED_PLAN.md` F4. From
3.27.4 the command prints one line saying so).

**P10 / E0 — the renderer compiles against the BCL alone: YES.** True ASP.NET/DI coupling is 18 files
(`using Microsoft.AspNetCore|Microsoft.Extensions`, or `IHttpContextAccessor`) plus 3 capture-side files that
reference excluded options types. The only seams into the renderer are two options classes holding an
`IHttpContextAccessor?` property (`SqlTrackingOptionsBase`, `TestTrackingMessageHandlerOptions`).
`InternalFlowSpanCollector`/`ActivityListener` looked coupled but only contain `"Microsoft.AspNetCore"` as an
activity-source *string*; the renderer needs them (`ReportGenerator.cs:261`). A native exe built from that
trimmed source set produced output **byte-identical to real `kronikol ingest`** except `kronikolVersion` and
suite→stableId. **Trimming changes nothing.** Projects and the 21-file exclusion list are in the session
scratchpad (`e0/`, `e1/native`, `e1/render4`).

**P10 / E1 — WASI build: YES. WASI run: boots and executes the managed pipeline; blocked at one dependency.**
- Private SDK 10.0.300 + `wasi-experimental` (installed to the scratchpad; no admin needed). `dotnet publish
  -r wasi-wasm` **succeeds**; only CA1416 warnings, all in `PlantUml/NodeJsPlantUmlRenderer.cs` (unused by ingest).
- **The output is a WASI Preview 2 COMPONENT, not a Preview 1 module** — header `0d 00 01 00`, world exports
  `wasi:cli/run@0.2.0`. `dotnet.wasm` (12,463,980 bytes) is the *prebuilt* Mono runtime from the runtime pack;
  the app is `managed/` (173 DLLs). The Wasi.Sdk targets hard-code `wasm32-wasip2`; **no Preview 1 output exists
  in .NET 10.0.300** (`WasmSingleFileBundle` only decides whether `managed/` is embedded).
- The component imports **`wasi:http/*` and `wasi:sockets/*`** although the ingest path never uses them
  (§8.1's floor test would have flagged this); wasmtime refuses to link without `-S http -S inherit-network`.
- **Hosts.** `node:wasi` and `wasmtime-py` are P1-only and cannot load it. **Chicory is P1-only** (README:
  WASIp1, no component model) → **cannot host .NET 10's output; the Kronikol4J plan's pure-Java-host premise
  is false as measured.** `jco 1.33.0` boots Mono but dies in `assembly.c:2718` even with `managed/` reachable
  and the right argv — a preview2-shim fidelity gap. **`wasmtime` 48.0.2 works**: `-S http -S inherit-network
  -S tcp -S udp --dir .::/ --dir managed::/managed --dir <work>::/work --env
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet.wasm Render /work` (**argv[1] must be the entry-assembly
  name** — the SDK's own `run-node.sh` passes `Render $*`; and Git Bash must not path-convert `/work`). Mono
  boots in ~200 ms, loads the assemblies, reads the NDJSON, and runs `IngestPipeline` until
  **`System.PlatformNotSupportedException: SystemSecurityCryptography_PlatformNotSupported`**.
- **The blocker is exactly two call sites**: `SHA256.HashData` in `Reports/ScenarioStableId.cs:51` and
  `MD5.HashData` in `Ingestion/InteractionRecord.cs` (hashing a non-UUID `requestResponseId`). Mono on WASI has
  no crypto backend. Fix = managed SHA-256/MD5 with identical output (stableIds are pinned history; the
  algorithm cannot change). **Byte-identity of the WASI output is therefore still unproven**, but everything
  up to those calls executed and nothing else in the path is known to be unsupported.
- **E2 (speed)**: not yet measurable — the run dies before rendering. Host start to the crypto call: 430 ms.

**What this changes.** F6's artifact is a P2 component; "build P1, adapt up" is unavailable.
`KRONIKOL4J_PORTABILITY_PLAN.md` §4/§4.2/§4.3/A7/A12–A16 are corrected by banner. Java's host becomes
`wasmtime-java` (JNI; component support unverified) or a future Chicory with components — the
JVM-portability argument for wasm *over* a native binary is weakened, not reversed: one JNI runtime is still
one dependency, not a RID matrix. F6 gains two work items: the managed-hash fallback, and the §8.1 floor test
(which already fails on the http/sockets imports).

---

## 12. Sharing beyond the renderer, settled 2026-09-15

**Status: decisions recorded, nothing implemented, still NOT green-lit.** This section answers a question
§1 leaves open: once rendering, query, the pure functions, options and the corpus are on the shared
side, what else in the left column can be shared or generated, and at what cost in fidelity. Nine
levers were examined, each first for its fundamental disadvantages and then for whether those could be
worked around. What follows is the settled position, the reasoning that produced it, and the fidelity
accounting. Where it touches F1, F2, F3, F4, F6 or F9 it amends them; §12.7 lists the amendments and
adds F10.

### 12.1 The settled positions

| # | Lever | Decision | What it moves to the shared side | Residual, honestly |
|---|---|---|---|---|
| L1 | **The taps as a shared end-to-end artifact** (`Kronikol.Extensions.ProxyTap` + `Kronikol.Extensions.TcpTap`, published once as `kronikol tap`, a binary or container image every platform's Testcontainers can start) | **Take, as F10.** A complement to in-process capture for end-to-end suites, on every platform. **Not a replacement for in-process capture** on any platform | HTTP, Redis and Mongo wire capture with bodies; Postgres and MySQL decoders added once instead of per driver per language. For Node this is the answer to "no JDBC equivalent" in end-to-end suites | Database hops carry inferred attribution (time window or in-flight registry) under parallel workers; a tap sees bytes, not intent (no SDK operation names, nothing in-memory); TLS means the tap terminates the connection, so cloud-hosted dependencies need certificate trust. All three are why it is a complement |
| L2 | **W3C baggage as a second carrier for test identity** | **Take, reframed as L1's companion.** Each platform keeps its own ambient store; at egress it stamps the existing `test-tracking-*` headers *and* a baggage entry holding only `test-id` and `trace-id`; the receiving side reads either | Propagation through OTel-instrumented services nobody added Kronikol to | Not a saving on in-process code. Register a context manager only when none exists. Test name stays in the headers (baggage limit 8 KB) |
| L3 | **OTel instrumentation packages as the capture seam** (their `requestHook`/`responseHook`) | **Rejected.** One instrumentation instance per library and it is the user's; hook signatures are 0.x; hooks hand over objects, not bodies, so the teeing work is not saved | Nothing | Node's conflict-free seam is core `diagnostics_channel` (http and undici channels, body channels included, confirmed in `NODE_PORT_PLAN.md` App. C); Python patches. OTel stays the tail bridge it already is (§11, Decision 5) |
| L4 | **HAR import** (`kronikol ingest --har`) | **Take.** Playwright records HAR in every binding; one importer makes browser-side capture free on every platform | Browser-side view of every request the test's browser made | A HAR is flushed on context close and has no per-request id. Duplicates against a server-side capture are folded by extending `InteractionMerger`'s pairing rule (caller, service, verb, last path segment, interval overlap) with a `browser` side; the merged arrow carries both views and says so. Playwright's request/response events are the better ~50-line per-platform version with real-time bodies and a per-request id |
| L5 | **Reporter-format importers** | **Take CTRF only**, documented as the *no-adapter fallback*. Not JUnit XML (no per-test start time in most dialects, so window attribution is impossible), not TRX, not per-framework JSON | Test outcomes and test-level windows for a framework nobody wrote an adapter for | **Does not remove the ~100-line adapter for any supported framework**: test ids must still be stamped in-process. CTRF steps carry name and status, no timestamp, so step windows degrade to test windows (§12.4). Only useful single-worker or where CTRF's per-test start/stop makes windows unambiguous |
| L6 | **More raw facts to the renderer** (extends F3) | **Take, under one rule (§12.2).** Stack traces cross as a structured `frames[]` plus raw text; CI environment crosses as the variables named in one shared allowlist file; the failing line's source snippet is attached at capture | Stack-trace interpretation, CI detection (closes the P8 "reads env at render time" item), the failing line in `Failures.md` | Each is 20 to 40 lines per platform. Python, Java and .NET give structured frames natively; JS needs a short parser and resolves source maps in-process, where the maps are |
| L7 | **Generate per-platform code from the schemas** (F2/F4) | **Take.** Record types, the options object with defaults, the validator and the options reference are generated; defaults live in the schema and only non-default values are written | 95 options × 4 languages never typed by hand; one regenerating PR per schema change in the monorepo | **Callbacks cannot cross.** `RequestResponseMidProcessor`/`PostProcessor` become a rule list (§12.3). Pre-stage processors move to capture (they already receive raw content). Service-name resolution: the resolved name crosses where a platform consulted its own config (P9) |
| L8 | **One wiki** | **Take.** Feature pages describe the report and name options by their schema name; each platform has short install/configure pages mapping names to its idiom; per-language snippets in `<details>` blocks where they must sit together | Roughly half the current wiki (the non-`Integration-*` pages) stops being per-platform | Supersedes `NODE_PORT_PLAN.md` §12.1's parallel Kronikol.js wiki |
| L9 | **One rendering path on .NET** (extends F1/F5) | **Take, in-process.** `IngestRequest.Interactions` already accepts record objects, so the in-process sink hands records to `IngestPipeline` in memory and the renderer runs in the same process. NDJSON becomes one producer of records, not the only one | The in-process-vs-ingest parity tax disappears: the round-trip test becomes "on-disk records equal in-memory records" | No serialisation cost, no process boundary, failure isolation and debugging unchanged, the public façade kept. Additive fields need no `captureFormatVersion` bump (unknown fields are ignored). The F5 list is the whole remaining cost |

### 12.2 The rule that decides L6 and its successors

> **Anything that needs the host's files, symbols or environment is gathered by the capturer as data
> and interpreted by the renderer. The capturer never interprets; the renderer never reads outside the
> captures directory.**

This is §1's "raw facts cross the boundary" principle with its converse made explicit. It was needed
because the naive form of L6 (ship the raw trace, let the renderer parse it; let the renderer read the
source file) breaks two properties the plan depends on: the renderer stays language-agnostic only if
it does not hold a stack-trace parser per language, and it stays text-in/text-out and deterministic
(§8.1 of the Kronikol4J plan, F8's corpus) only if its output depends on nothing outside its input.
Source maps are the sharpest case: a TypeScript trace points into `dist/` unless the process that has
the maps resolves it.

### 12.3 Processors become a rule list

Today `ReportConfigurationOptions.RequestResponseMidProcessor` and `RequestResponsePostProcessor` are
`Func<string,string>` applied at render time to the diagram note only: mid after JSON pretty-printing
and before focus markup and header wrapping ([PlantUmlCreator.cs:335](../src/Kronikol/PlantUml/PlantUmlCreator.cs#L335)),
post on the final note body (`:337`). The data outputs never see either result.

A function cannot cross into `kronikol.render.json`. The replacement is a **rule list** in the options
file, applied by the renderer at the same two points:

```json
"noteRules": [
  { "stage": "mid",  "kind": "regex",        "pattern": "Bearer [A-Za-z0-9\\-._~+/]+=*", "replacement": "Bearer ***" },
  { "stage": "mid",  "kind": "hash",         "pattern": "Bearer [A-Za-z0-9\\-._~+/]+=*", "prefix": "Bearer #", "length": 8 },
  { "stage": "mid",  "kind": "redactMiddle", "pattern": "...", "keepStart": 7, "keepEnd": 7 },
  { "stage": "post", "kind": "regex",        "pattern": "<[^>]+>", "replacement": "" }
]
```

Measured against the wiki's recipes (`Filtering-and-Redacting-Diagram-Content.md`,
`Content-Formatting.md`): bearer redaction, JSON token fields, cookie shortening, PII masking, HTML
stripping and combined processors are `regex` rules; hash shortening and redact-middle are the two
other kinds. **One recipe is logic, not a pattern: JWT claims extraction** (`Bearer [sub=user1,
role=admin]`), which decodes the token. It is either a `jwtClaims` rule kind of its own or it is lost
on non-.NET platforms.

Two traps in the alternative (running the user's function at capture) that make the rule list the
right answer for mid and post:

- **Input form.** The wiki's token recipes match `"access_token": "` with the space after the colon,
  which is the *pretty-printed* form. The same regex on the compact body at capture silently matches
  nothing.
- **Scope.** A capture-time transform changes the body in every artifact, including the data files
  the query engine reads, where today it changed only the note. For redaction that is the improvement
  `NODE_PORT_PLAN.md` §3.13 argues for; for a readability transform (strip HTML, collapse an array) it
  is a regression, because the raw body is gone from the record.

**On .NET nothing changes.** The in-process path (L9) passes the live `ReportConfigurationOptions` to
the pipeline, so the delegates keep running at their points. The delegate options stay, and where a
delegate is one of the recognised shapes they can compile into the same rule list; a user who captures
to NDJSON and renders later gets the rule list only.

**Kronikol4J divergence ledger entry, to be written when F6 lands.** `kronikol4j-diagram`'s
`NoteProcessors` record (six `UnaryOperator<String>`: pre/mid/post × request/response, reachable
from `ReportOptions`, the parity doc's "deliberate Java seam") is deleted with the module. Pre-stage
lambdas move to the Java capturer unchanged in input, wider in scope; mid and post become the rule
list; arbitrary logic at those stages is no longer possible from Java. No built-in processor ships in
Java today, so this removes a seam, not a shipped feature. What a Java user could show in a note
before and after:

| Note content | .NET | Java after F6 |
|---|---|---|
| `Bearer ***` · `Bearer #a3f29c01` · `Bearer eyJhbGc…KxwRJSM` · `"access_token": "***"` · emails masked · HTML stripped | delegate | rule |
| `Bearer [sub=user1, role=admin]` (claims decoded) | delegate | `jwtClaims` rule kind, or not possible |
| Anything a user invents that a pattern cannot express | delegate | not possible |

### 12.4 The no-adapter fallback (L5), precisely

A `step` record with a timestamp drives five things: the step delimiter bar in the sequence diagram,
phase tagging via `PhaseForStep`/`ApplyPhaseFromSteps` (which is what `SeparateSetup` and
`HighlightSetup` act on), assertion nesting under the containing step, per-step attachments and
status, and "the calls made inside the failing step" in `Failures.md`. A reporter file gives per-test
start and stop and, at best, step names with a status. In the fallback the step list still appears in
the report, but the diagram is one undivided block per test, every interaction's phase is Unknown,
assertions appear only as the test's error text, and `Failures.md` lists the test's calls rather
than the failing step's. Cucumber Messages (already read) and Playwright's JSON reporter carry
timestamped steps and do not suffer this; CTRF, TRX and JUnit XML do. The adapter's step hook is the
only cure, because a step timestamp exists only if something in the process recorded the clock.

### 12.5 Fidelity accounting

**Against today's .NET: nothing lost, on two conditions.** (1) F5 closes the round-trip completely,
which is already the plan's gate; until it does, `InProcess` stays the default. (2) The in-process
path keeps passing the live options object, so the delegates keep running (§12.3).

**Against Kronikol4J today: one regression**, the `NoteProcessors` seam (§12.3). Pre-1.0, no users to
break, recorded on the ledger as a deliberate removal with the rule list named as the replacement.

**Ceilings, not losses**, each where nothing existed before: the OTel tail has no bodies (§4.6); the
CTRF fallback has test-level attribution and no step windows (§12.4); tap-captured database hops
under parallel workers carry inferred attribution, marked in the report (§4.3). Everything else in
§12.1 is additive.

### 12.6 What was rejected, and why it stays rejected

- **OTel hooks as the seam (L3).** Singleton conflict with the user's own instrumentation has no
  workaround; hooks do not save the body-teeing work, which the Node plan identifies as the hard
  part. Trading "track undici" for "track `@opentelemetry/instrumentation-undici` at 0.x" moves the
  breakage onto a schedule nobody here controls.
- **Host callbacks from the renderer** (a wasm component import the shim implements, so the renderer
  could call a user's function). Restores arbitrary-logic processors on every platform, but every host
  shim must implement the import, the NativeAOT fallback cannot do it at all, and it breaks §8.1's
  floor. The rule list plus new rule kinds as they become common is the cheaper answer.
- **The tap as an in-process replacement.** §12.1 L1's residual column. Attribution certainty and
  in-process semantics are the product; a tap has neither.

### 12.7 Amendments to the foundations

| Milestone | Amendment |
|---|---|
| **F1 / F5** | The .NET in-process path feeds `IngestPipeline` with record objects in memory (L9). `CaptureMode.Both` stays for the round-trip test; the long-term default is the in-memory single path once F5 is green |
| **F2** | The contract gains: `frames[]` (structured stack frames beside the raw trace), `environment` (values for the variables in `parity/contract/ci-env.json`, an allowlist the capturer reads), `sourceSnippet` on failure and assertion records, and the `noteRules` schema (§12.3). A `browser` value for `capturedBy` (L4) |
| **F3** | Stack-trace interpretation and CI detection move renderer-side, fed by the fields above. The rule in §12.2 is added to F3's text |
| **F4** | Defaults live in the schema; per-platform options objects are *generated* with those defaults and serialise only non-default values. Pre-stage processors are documented as capture-side |
| **F6** | Two importers on the shared artifact: HAR (L4) and CTRF (L5). The merger gains the browser-side pairing |
| **F9** | The template's item 1 (ambient context) gains the dual stamp (headers + baggage, L2). The generator emits the generated record and options types rather than stubs |
| **F10 (new): the shared tap artifact** | `kronikol tap`: ProxyTap + TcpTap published once as a tool (NativeAOT binary and/or container image), writing `interactions.ndjson` to a directory. Adds Postgres and MySQL wire decoders. Outside the §8.1 floor by nature (sockets), so a **second** artifact, never part of the renderer. **Done when** every platform's end-to-end example runs against it with no capture code of its own beyond stamping identity, and `parity/capture/` has tap fixtures for all five protocols |
| **Docs** | One wiki (L8); `NODE_PORT_PLAN.md` §12.1 superseded |

### 12.8 Assumption ledger additions

| # | Claim | Status |
|---|---|---|
| S1 | CTRF step entries carry name and status but no timestamp | **assumed** from the specification as remembered and from `CtrfReportGenerator.cs`, which emits run start/stop and per-test duration only. Verify against the CTRF schema before F6's importer |
| S2 | Node's `undici:*`/`http.client.*` diagnostics channels are subscribable including body channels | **confirmed** in `NODE_PORT_PLAN.md` Appendix C (Node v25, undici 8.5.0) |
| S3 | Playwright records HAR and exposes request/response events with bodies in all four language bindings | **assumed** from general knowledge; not verified per binding |
| S4 | `IngestRequest.Interactions` accepts record objects directly, so L9 needs no serialisation | **measured**: `IngestPipeline.cs:14` |
| S5 | `InteractionMerger` pairs on caller, service, verb, last path segment and interval overlap, and reads `CapturedBy` | **measured**: the class's own remarks |
| S6 | The wiki's processor recipes are all pattern-shaped except JWT claims extraction | **measured**: both wiki pages read in full |
| S7 | Kronikol4J ships no built-in processor; `NoteProcessors` is a seam only | **measured**: `NoteProcessors.java` and `REMAINING_PARITY.md` L1132 |
| S8 | A baggage entry of id + trace id stays under the 8 KB limit | **reasoned**: two short tokens |

**S1 is the one to check first**, because it decides whether the CTRF importer can carry step windows
after all.
