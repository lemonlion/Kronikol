# Plan: PlantUML "Syntax Error?" on diagrams containing a very long message label

## 1. Symptom

In `sidekick-intelligence-e2e/.logs/kronikol/TestRunReport.html`, scenario
**"A cold insights request reaches BigQuery"**, the first fragment of the Sequence Diagrams section
renders as:

```
PlantUML version 1.2026.3beta6 / e69469b [From textarea (line 56) ]
@startuml
…
 Syntax Error? (Assumed diagram type: class)
```

The remaining 20 fragments of the same diagram render normally. Reported by a user against the
**BrowserJs** renderer; the Node renderer (`PlantUmlRendering.NodeJs`) fetches the *same*
`plantuml.js` from `TrackingDefaults.PlantUmlJsCdnBase`, so it reproduces identically and was used
as a scriptable stand-in throughout this investigation.

## 2. Root cause — measured against the real engine

**PlantUML's message-statement parser refuses any statement longer than 2000 characters.** It does
not report "too long"; the statement matches no rule, the parser gives up on the whole diagram, and
the engine emits `Syntax Error?`.

### 2.1 The limit is per-statement-kind, not a global line limit

This is the part that matters for the fix — the first pass of this plan assumed a global 2000-char
line gate, and that is **wrong**. Measured (trimmed statement length, engine
`lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.3beta6-patched`):

| statement | 1500 | 2000 | 2001 | 3000 | 6000 | cap |
| --- | --- | --- | --- | --- | --- | --- |
| `a -> b: …` | ok | ok | **SYNTAX** | SYNTAX | SYNTAX | **2000** |
| `a --> b: …` | ok | ok | **SYNTAX** | SYNTAX | SYNTAX | **2000** |
| `a -[#F39C12]> b: …` | ok | ok | **SYNTAX** | SYNTAX | SYNTAX | **2000** |
| `loop …` | SYNTAX | SYNTAX | SYNTAX | SYNTAX | SYNTAX | **~1476** |
| `alt …` | SYNTAX | SYNTAX | SYNTAX | SYNTAX | SYNTAX | **~1475** |
| `hnote across …` | ok | ok | ok | ok | ok | none observed |
| `note over a : …` | ok | ok | ok | ok | ok | none observed |
| note **body** line | ok | ok | ok | ok | ok | none observed |
| `'` comment line | ok | ok | ok | ok | ok | none observed |
| `!$v = "…"` | ok | ok | ok | ok | ok | none observed |

Consequences for the fix:

- Only **message/arrow statements** need the 2000 cap.
- **Block openers (`loop`, `alt`, …) have a *lower* cap (~1471 characters of label).** Kronikol's
  loop labels are short (`loop ×3 · 0–3 ms`), so this is not live today, but a backstop must use the
  lower number for these.
- **`hnote across` is uncapped** — so the step-delimiter bars do **not** need truncating. That is
  fortunate: a Gherkin step with a doc-string can legitimately be long, and capping it would have
  been a gratuitous regression.
- **Note bodies are uncapped** — a backstop must leave them alone.

### 2.2 It is the whole trimmed statement, not just the label

- `a -> b: ` (8-char prefix) → max label 1992 ⇒ statement 2000.
- `aaaaaaaaaaaaaaaaaaaa -> b: ` (27-char prefix) → max label 1973 ⇒ statement 2000.

Leading/trailing whitespace is not counted (a valid short arrow padded to 2500 characters with
trailing spaces parses fine), so the cap applies to the **trimmed** statement.

### 2.3 It is not an escaping problem

`a -> b: hello\nworld` parses fine. `--` needs no escaping. Escaped `\n` inside a label is a
**two-character escape** — it wraps the label visually but leaves the statement one physical line,
so wrapping does not help and in fact makes the statement *longer*.

### 2.4 The offending statement

`puml-9` line 60 of the generated source — **5,410 characters**:

```
dataInsights -[#F39C12]> redis: DELETE: /data-insights-api:_v2:customer-local-competitors-charts-agg-…\n        rWeeks-…
```

A Redis `DELETE` of 41 cache keys, so the URL path is ~5,300 characters. Generated at
[PlantUmlCreator.cs:252-253](src/Kronikol/PlantUml/PlantUmlCreator.cs#L252-L253):

```csharp
var pathAndQuery = effectiveUri.PathAndQuery;
if (pathAndQuery.Length > maxUrlLength)
    pathAndQuery = string.Join("\\n        ", pathAndQuery.ChunksUpTo(maxUrlLength));
```

`maxUrlLength` (default 100) controls **where the label wraps for display**, not **how long it may
get**. 5,300 characters become 53 display chunks joined by a literal `\n        ` — one 5,410-char
statement. Nothing bounds the total.

### 2.5 Why only fragment 0 fails

Client-side splitting (`splitWithChunkedNotes` in
[plantuml-browser-render-script.js](src/Kronikol/Reports/plantuml-browser-render-script.js)) splits
*between* lines, by estimated height and note size. It cannot split *within* a statement, so the
over-long arrow lands intact in whichever fragment holds it — fragment 0 — and only that fragment
fails.

### 2.6 Blast radius

Scanning every embedded diagram source for over-long statements:

| report | diagrams | over-long statements |
| --- | --- | --- |
| `TestRunReport.html` | 20 | 1 (`puml-9` line 60) |
| `Specifications.html` | 19 | 1 (`puml-8` line 60 — the same trace) |
| `ComponentDiagram.html` | — | no `puml-data` island; different embedding, not scanned |

A rare-but-real edge case (any request whose URL or query string is very long), not systemic
breakage. Both sequence-diagram reports come from the same generator, so one fix covers both.

## 3. Reproduction (scripted, no browser)

1. Decode `<script id="puml-data">` from the report (gzip + base64 per `puml-N` key) → `cold.puml`.
2. Apply the default filter state (assertions hidden) and run the report's own
   `splitWithChunkedNotes` in Node → 21 fragments; `frag-0.puml` is the failing one.
3. Render `frag-0.puml` with `plantuml-render.js` → `Syntax Error? … [From textarea (line 56)]`.
4. Truncate that one statement to 1,991 characters → renders, 183 KB of SVG.

Step 4 is the proof that statement length alone is the cause.

## 4. Fix

### 4.1 Layer 1 — bound the request arrow label (the real cause)

In [PlantUmlCreator.cs](src/Kronikol/PlantUml/PlantUmlCreator.cs):

- Add engine-limit constants, documented as *measured* values:
  `MaxMessageStatementChars = 2000`, `MaxBlockLabelChars = 1471`.
- Cap the **final** label against `MaxMessageStatementChars − prefixLength`, where the prefix is the
  real `{callerShortName} -{arrowColor}> {serviceShortName}: ` (its length varies with aliases and
  the colour token), leaving a small safety margin.
- Cap **after** the two things that grow the label, or reserve room for them:
  - the GraphQL suffix `\n({graphQlLabel})` ([lines 258-260](src/Kronikol/PlantUml/PlantUmlCreator.cs#L258-L260));
  - the internal-flow wrapper `[[#iflow-{guid} {label}]]` ([lines 262-263](src/Kronikol/PlantUml/PlantUmlCreator.cs#L262-L263)),
    ~45 characters. If the cut lands inside the wrapper it must be re-closed (`]]`), or the label
    truncated before wrapping.
- Count the `\n        ` separators (9 chars each) that `ChunksUpTo` joins with — they are part of
  the statement length.
- **Do not lose the URL.** For a `DELETE` with no body the path *is* the payload. When the label is
  truncated, append the full `PathAndQuery` to the request note (today it carries only headers +
  body — see `FormatNoteContent`). Note bodies are uncapped and already chunked at
  `MaxNoteChunkChars` (80) for `wrapWidth`, so the full path stays visible, searchable and copyable.

### 4.2 Layer 2 — statement-aware backstop in `DiagramBuilder`

`DiagramBuilder` ([PlantUmlCreator.cs:~960-1141](src/Kronikol/PlantUml/PlantUmlCreator.cs#L960-L1141))
is the one funnel every diagram line passes through — `AppendLine` for generated statements and
`Append` for raw passthrough of `trace.PlantUml` ([lines 182 and 194](src/Kronikol/PlantUml/PlantUmlCreator.cs#L182-L194)).

Add a guard there that classifies each **physical** line and caps only what the engine actually caps:

| line | action |
| --- | --- |
| inside a note block (`note`/`hnote` … `end note`/`endhnote`/`endrnote`) | **leave alone** |
| `hnote across …`, `note over X : …`, comments, preprocessor | **leave alone** |
| message/arrow statement | truncate to `MaxMessageStatementChars` with `…` |
| block opener (`loop`/`alt`/`opt`/`group`/`par`/`critical`/`break`/`partition`) | truncate to `MaxBlockLabelChars` with `…` |

The note-state tracking already exists in the client-side splitter (`scanOpenBlocks`) and can be
mirrored. This makes "no emitted message statement exceeds 2000 characters" an enforced invariant
rather than something each call site must remember.

### 4.3 Other message emitters to close

Only **message** emitters need this; the step-delimiter and assertion-note emitters are `hnote`
statements and are safe (§2.1).

| Emitter | Location | Risk |
| --- | --- | --- |
| User-action arrow label | [PlantUmlCreator.cs:223](src/Kronikol/PlantUml/PlantUmlCreator.cs#L223) | **Real** — `effectiveMethod.Value` with `\n` → `\\n`; a long Playwright locator or action description is one message statement |
| Response arrow label | [PlantUmlCreator.cs:377](src/Kronikol/PlantUml/PlantUmlCreator.cs#L377) | Low — a titleized status code |
| Loop / partition labels | [PlantUmlCreator.cs:266-269, 202](src/Kronikol/PlantUml/PlantUmlCreator.cs#L266-L269) | Low, but the ceiling is the *lower* ~1471 |
| Step delimiter bar | [InteractionRecord.cs:277](src/Kronikol/Ingestion/InteractionRecord.cs#L277) | **None** — `hnote across` is uncapped; do **not** truncate |
| Assertion note | [InteractionRecord.cs:288](src/Kronikol/Ingestion/InteractionRecord.cs#L288) | **None** — short header, body is note content |
| Participant declarations | `CreateEntitiesPlantUml` | Low — service names |

### 4.4 Optional — better failure surfacing

`describeEngineFailure`
([plantuml-browser-render-script.js:~830](src/Kronikol/Reports/plantuml-browser-render-script.js#L830))
already detects `Syntax Error?` and attaches the raw source in a `<details>` — that is the only
reason this was diagnosable. Extend it: when a failing fragment contains a message statement over
2000 characters, say so explicitly. Cheap, and it turns a recurrence into a one-glance diagnosis.

## 5. Test plan (TDD, red first)

### 5.1 Unit — `tests/Kronikol.Tests/PlantUml/PlantUmlCreatorTests.cs`

1. **Red:** a trace whose `Uri.PathAndQuery` is ~5,000 characters ⇒ assert every emitted message
   statement is ≤ 2000 characters. Fails today.
2. The label still parses structurally (`caller -color> service: …`) and ends with the marker.
3. The full path survives in the request note.
4. A long user-action label is capped.
5. A long **note body** is *not* capped (guards Layer 2 against over-reaching).
6. A long **`hnote across` step bar** is *not* capped (same).
7. The cap accounts for the `[[#iflow-…]]` wrapper **and** the GraphQL `\n(query X)` suffix — enable
   both in one case; this is where an off-by-one will hide.

### 5.2 Integration — real engine

`tests/Kronikol.Tests/PlantUml/NodeJsPlantUmlRendererTests.cs` already has the
`Assert.SkipWhen(!IsNodeAvailable())` + `[Trait("Category","Integration")]` pattern. Add:

- **Boundary pins:** `a -> b: {2000 chars}` ok / `{2001}` `Syntax Error?`; `loop {1471}` ok /
  `{1472}` fails. These document the measured limits and will flag a future engine bump that moves
  them.
- **Regression:** generate a diagram from a 5,000-character-URL trace, render it, assert no
  `Syntax Error?`.

### 5.3 Corpus invariant

A single test that walks every diagram the existing test corpus generates and asserts no message
statement exceeds 2000 characters (and no block label exceeds 1471). Cheap, and it catches any
future emitter that forgets.

### 5.4 E2E — `tests/Kronikol.Tests.EndToEnd/`

Per `CLAUDE.md`, UI behaviour gets a Playwright test: build a report containing a long-URL trace,
open it, wait for the fragments, assert no `[data-engine-failure="syntax"]` element exists. House
rules: `PollingInterval = 200` on `WaitForFunctionAsync`, `.First`/`.Nth(n)` for per-diagram
controls, no `Force = true`, no network mocking.

## 6. Open questions worth settling before/while implementing

1. **Do the real-Java renderers share the cap?** `PlantUmlRendering.Server` (a PlantUML server) and
   `Local`/Ikvm run real Java PlantUML, not the TeaVM build. If the 2000 cap lives in PlantUML's own
   regex layer it applies there too; if it is a TeaVM/fork artifact it does not. **Unverified** —
   requires a call to a PlantUML server or a local Ikvm run. Fixing at generation time is correct
   either way, so this does not block the work; it only affects how the limits are documented.
2. **Component diagram edge labels.** [ComponentDiagramGenerator.cs:190](src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs#L190)
   builds `[[#iflow-rel-… {methodsPart}]]\n…` edge labels that grow with the method count. Component
   diagrams use a different parser with its own limits — **unmeasured**. Worth a short probe.
3. **What is the 1471 for `loop`/`alt`?** An oddly specific number, and lower than the message cap.
   Not blocking (Kronikol's block labels are short), but it suggests the limit is not a single
   documented constant, so the pins in §5.2 matter.

## 7. Follow-ups

- **Changelog + version bump** across all packages, per `CLAUDE.md`.
- **Wiki** (`../Kronikol.wiki`): document the measured statement limits and the new truncation
  behaviour, alongside the existing note-truncation options.
- **Kronikol4J**: the Java port is byte-for-byte identical on report/diagram output. Mirror this cap
  there or the two implementations diverge on long-URL traces.
- **Configurable cap?** Only if a user asks — the limit is a hard engine constraint, so a knob
  mostly invites misconfiguration.
