# Plan: note payload line breaks (bug) and a full-width note control (feature)

**Opened 2026-09-11**, from one user report against a production `TestRunReport.html`: ClickHouse
query notes come out with line breaks in the middle of words, and there is no way to widen a note so
a SQL query can be read.

**Part A shipped in 3.0.84** (2026-09-11, tag `v3.0.84`). **Part B shipped in 3.0.85**
(2026-09-11) — M2-M7 executed in full, with the §2.9 recommendation adopted: the monospace toggle is
a sibling of the width control and the note button's first glyph, because it is the larger measured
readability win and needs none of the container arithmetic.

**Executed, with the decisions the measurements forced:**

| Plan item | What shipped |
|---|---|
| §2.2 per-note, not per-diagram | `.kronNoteWide` / `.kronNoteMono` stereotypes + one injected `<style>` block per re-render source; nothing at generation time |
| §2.3 plug-in points + the regex hazard | `_noteWidths` / `_noteFonts` beside `_noteFormats`; all four note-header matchers widened to `(?:<<[^>]*>>)*` in the same commit; the search-index opener deliberately untouched |
| §2.3 (new) | The three source-rebuild chains were factored into **one** `composeNoteSource` so they cannot fork — the 3.0.67 lesson applied before it could bite. The pins that asserted the inlined chain now assert that helper |
| §2.4 width arithmetic | `currentNoteWidth + (containerInnerPx - drawnSvgWidth)`, clamped 320-4000, measured through the zoom code's existing natural-width reader, with exactly one correction pass |
| §2.5 UI | `M`/`A` and `↔`/`→←` in the hover cluster via a shared glyph factory; `Aa / Mono` and `Fit / Full` selects at report and scenario level through `BuildScenarioDiagramToolbar` |
| §2.6 options | Both halves taken: `ReportToggleDefaults.NoteFont` / `.NoteWidth`, **and** `DiagramNoteWrapWidth` — the latter floored at 720 rather than deriving the chunk size from it, which keeps `MaxNoteChunkChars` a constant and every form body byte-identical |
| §2.8 tests | `NoteAppearanceTests` (engine contract + behaviour off the painted SVG), `NoteAppearanceScriptTests`, `DiagramNoteWrapWidthTests`, resolver/markup facts |
| §2.9 + Q11/Q13 | Monospace shipped first, `Courier New` named explicitly |
| §2.10.5 zoom | Left alone, as recommended — still a pre-existing, separate change |
| §2.11 HTML panel | Still deliberately not taken |

One thing the plan did not anticipate, found by the E2E round-trip fact: a widened note stops
wrapping, so the painted-rows eligibility test that decides whether to *offer* the control also hid
the control that undoes it. An already-widened note now always keeps its button.

Everything in Part A §1.2–§1.6 and Part B §2.1, §2.4, §2.9 is *measured* — against the pinned engine
(`tools/render-bench/core-1.2026.8beta1-0e4f452.js`, the build Kronikol ships), against **real Java
PlantUML** through the IKVM renderer, and against the real generator
(`PlantUmlCreator.GetPlantUmlImageTagsPerTestId`). Repro commands are in the appendix.

Read §2.9 before §2.3–§2.8: the largest readability win measured here is **not** width. §2.10 holds
the six follow-up measurements, all now taken — one of them (font resolution) changes the design.

**`PlantUmlRendering.Server` is being phased out.** Noted where it changes a judgement below (§2.6,
§2.1): it removes one *justification* for a generation-time option but none of the constraints, because
the path that actually hands Kronikol's source to real Java PlantUML is the context menu's
*Copy / Open PlantUML source*, which is a report feature independent of how the report was rendered.

---

# Part A — the bug: note payload lines are chunked at 80 characters, blind

## 1.1 Symptom

The user's ClickHouse note (a `WITH … SELECT …` of ~9 KB) renders with breaks like:

```
         WHEN {comparisonPeriodOnPeriod:String} = 'WoW' THEN subtractDays(t.tran
saction_period, 7)
            WHEN {comparisonPeriodOnPeriod:String} = 'MoM' AN
D {cadence:String} = 'Weekly' THEN subtractWeeks(t.transaction_period, 4)
```

`t.tran|saction_period`, `AN|D`, a line consisting of three spaces, `SEL|ECT`. The breaks are not at
word boundaries and not at a fixed column of the *source* line.

They **are** at a fixed column of the *stream*: a newline every 80 characters counted continuously,
with the payload's own newlines counted as ordinary characters and never resetting the counter.
Arithmetic on the reported text confirms it exactly — e.g. `saction_period, 7)` (18) + its newline
(1) + `            WHEN … 'MoM' AN` (61) = **80**, and the next segment
`D {cadence:String} … subtractWeeks(t.transaction_period, 4)` (73) + newline (1) + the six-space
fragment = **80** again.

## 1.2 Root cause

[PlantUmlCreator.cs:849-859](src/Kronikol/PlantUml/PlantUmlCreator.cs#L849-L859) — every **request**
body that is not JSON falls through to the form-url-encoded formatter:

```csharp
parsedContent ??= TryFormatAsJson(content);
parsedContent ??= TryFormatTruncatedJson(content);

var payloadIsPreEscaped = false;
if (parsedContent is null)
{
    if (type is RequestResponseType.Response)
        parsedContent = content ?? string.Empty;     // responses: passed through verbatim
    else
    {
        parsedContent = FormatFormUrlEncodedContent(content, escapePayload);
        payloadIsPreEscaped = true;
    }
}
```

and [PlantUmlCreator.cs:1146-1161](src/Kronikol/PlantUml/PlantUmlCreator.cs#L1146-L1161) chunks it:

```csharp
return content?
    .Split("&")
    .SelectMany(x =>
    {
        var chunks = x.ChunksUpTo(MaxNoteChunkChars)…     // MaxNoteChunkChars = 80
        chunks[^1] += divider;                            // "<font color=\"lightgray\">&"
        return chunks;
    })
    .StringJoin(Environment.NewLine)
```

`ChunksUpTo` is [`string.Chunk`](src/Kronikol/Extensions/StringExtensions.cs#L8) — fixed-size slices
of the whole string. It has no concept of a line or a word, so a multi-line SQL body is sliced every
80 characters *including* its own `\n` bytes. That is precisely the reported shape.

SQL reaches this path because `SqlDiagnosticTracker.BuildRequestContent`
([SqlDiagnosticTracker.cs:362-371](src/Kronikol/Sql/SqlDiagnosticTracker.cs#L362-L371)) records the
raw command text (optionally plus a `-- Parameters:` line) as the request content — it is not JSON,
so `TryFormatAsJson` declines and the form-encoded branch takes it.

### Measured, against the real generator

Feeding a 9-line ClickHouse query through `GetPlantUmlImageTagsPerTestId` produces this note body
(`^M` = CR, from the `Environment.NewLine` chunk join; the payload's own breaks are LF):

```
note left
WITH base_with_comp AS (
        SELECT
          t.*,
          CASE
          ^M
  WHEN {comparisonPeriodOnPeriod:String} = 'WoW' THEN subtractDays(t.transaction^M
_period, 7)
            WHEN {comparisonPeriodOnPeriod:String} = 'MoM' AND {cade^M
nce:String} = 'Weekly' THEN subtractWeeks(t.transaction_period, 4)
          END^M
 AS comparison_date
        FROM `sme`.`location_performance_weekly` t
end note
```

## 1.3 What it costs, drawn

A 21-line, 1 285-character ClickHouse query, rendered on the pinned engine at the shipped
`skinparam wrapWidth 800`:

| variant | display lines | mid-word breaks | drawn width |
|---|---|---|---|
| today (80-char chunking) | 37 | 7 | 720 px |
| chunking removed | 22 | 0 (one clean wrap at a space) | 1 018 px |
| chunking removed, `wrapWidth 1600` | 21 | 0 | 1 389 px |
| chunking removed, `wrapWidth 2400` | 21 | 0 | 1 389 px (natural width reached) |

The chunking is doing the opposite of what a reader wants on both axes: it makes the note **narrower**
(720 px, because no line can exceed 80 characters) and **70 % taller**, and it pays for that with seven
broken identifiers.

So the bug fix alone recovers almost all of the readability; Part B handles the residue (single
lines longer than ~135 characters, of which real analytics SQL has plenty).

## 1.4 Reach — it is not a ClickHouse bug

Every **non-JSON request body** in every integration:

- all SQL extensions (ClickHouse ×3 packages, SqlClient, Npgsql, MySqlConnector, Oracle, Sqlite,
  Dapper, EF Core Relational) — anything `SqlTrackingVerbosityLevel` above `Summarised` records;
- XML / SOAP bodies;
- plain-text and CSV bodies;
- GraphQL when `GraphQlBodyFormat.Json` is configured or the parse fails;
- genuinely form-url-encoded bodies — the one shape the code was written for, which is unaffected.

**Responses are not affected** (they take the verbatim branch at
[PlantUmlCreator.cs:854](src/Kronikol/PlantUml/PlantUmlCreator.cs#L854)). The asymmetry is the
clearest evidence that applying form-encoding treatment to every request body was never intended.

## 1.5 Two collateral defects on the same line of code

Both measured through the real generator.

**A2 — the `&` divider corrupts any body containing `&`.** `content.Split("&")` is unconditional, so
every `&` in the payload becomes a grey `&` plus a forced line break:

```
input : WHERE flags & 4 = 4 AND name = 'a & b'
output: WHERE flags <font color="lightgray">&
         4 = 4 AND name = 'a <font color="lightgray">&
         b'
```

A SOAP body is hit twice over — once by the chunker mid-tag, once by the divider inside an entity:

```
input : <GetQuote><Symbol>ACME &amp; CO</Symbol></GetQuote>
output: ~<soap:B
        ody>
            ~<GetQuote>~<Symbol>ACME <font color="lightgray">&
        amp; CO~</Symbol>~</GetQuote>
```

The reader sees a grey `&` that was never in the payload, indistinguishable from Kronikol's own
markup — a fidelity break of the same class as the ones 3.0.79 and 3.0.83 fixed.

**A3 — copy-note-text leaks the divider markup.** The copy path builds its text from the note
*source* lines and strips only creole escapes and the grey header prefix
([context-menu-script.js:375-408](src/Kronikol/Reports/context-menu-script.js#L375-L408)); it does
not strip `<font color="lightgray">`. Copying a SQL note today therefore yields the literal tag plus
the 80-character breaks — i.e. SQL that will not run. "Copy the query and paste it into a client" is
the single most likely thing a reader does with a database note.

## 1.6 Ruled out (so the fix is not aimed at the wrong layer)

- **PlantUML's own `skinparam wrapWidth` does not break words.** Rendered on the pinned engine, a
  101-character indented SQL line stays whole and a 134-character one wraps at a space. It does drop
  the continuation line's indentation, which is cosmetic and separate.
- **The YAML view is not involved.** The note is plain text, not JSON, so `reconstructNoteJson`
  returns `null`, the note is ineligible for the JSON⇄YAML toggle and no `Y` button is offered —
  the existing E2E fixture already documents this ("note 0 is the request's SQL text, which is plain
  text and has no format button", [NoteYamlToggleTests.cs:17-19](tests/Kronikol.Tests.EndToEnd/NoteYamlToggleTests.cs#L17-L19)).
  What the user sees is the C# chunker, in whichever view is on screen.
- **`WrapUnbreakableRuns` is not the culprit** — it breaks only whitespace-free runs over 120
  characters, is line-aware, prefers punctuation boundaries and runs *after* the chunker
  ([PlantUmlCreator.cs:876](src/Kronikol/PlantUml/PlantUmlCreator.cs#L876)). It is the protection
  that makes removing the chunking safe.
- **A stray `\r` in a note line is harmless to the render.** Measured: a note whose lines end `\r\n`
  draws identically to one that does not (the browser render script splits on `\n` only, unlike
  `plantuml-render.js:383` which normalises first). Not a defect to fix here; noted so the CRLF in
  the sample above is not mistaken for one.

## 1.7 The fix

Make the form-encoded formatter run only on content that is actually form-encoded, and let the two
mechanisms that already exist do the rest: `WrapUnbreakableRuns` bounds unbreakable runs at 120
characters, and `skinparam wrapWidth` wraps at spaces at render time.

```csharp
if (parsedContent is null)
{
    if (type is RequestResponseType.Response || !LooksLikeFormUrlEncoded(content))
        parsedContent = content ?? string.Empty;   // escaped by the shared path below
    else
    {
        parsedContent = FormatFormUrlEncodedContent(content, escapePayload);
        payloadIsPreEscaped = true;
    }
}
```

```csharp
/// A form-url-encoded body is one physical line of percent-encoded `k=v` pairs: spaces are
/// encoded (`+` or `%20`), so a raw newline, space or tab means this is a text body — SQL,
/// XML, CSV, a GraphQL document — and must be shown as captured.
private static bool LooksLikeFormUrlEncoded(string? content) =>
    !string.IsNullOrEmpty(content)
    && content.IndexOf('=') >= 0
    && !content.AsSpan().ContainsAny(FormUrlEncodedDisqualifiers);   // '\n', '\r', ' ', '\t'
```

Nothing else in `FormatFormUrlEncodedContent` changes: real form bodies keep the `&` divider, the
per-chunk escaping and the 80-character chunking (a form body has no spaces, so chunking is the only
way to break it, and the 80 keeps each line under `MaxLineWidth` so an inline colour tag is never
split — the reason the constant is 80 in the first place, recorded at
[PlantUmlCreator.cs:20](src/Kronikol/PlantUml/PlantUmlCreator.cs#L20)).

**Why the non-form branch must not pre-escape.** Returning the raw content and letting
[PlantUmlCreator.cs:874-875](src/Kronikol/PlantUml/PlantUmlCreator.cs#L874-L875) escape it whole is
both simpler and *more* correct than escaping per chunk: `EscapeCreoleMarkup` sees the complete text
rather than 80-character slices, so a `~` can never be separated from the character it protects.
That also makes the request path byte-identical to the response path for the same body, which is the
contract a reader expects.

### Consequences, deliberate

1. **Note bodies get wider and shorter.** Measured on the sample query: 720 → 1 018 px wide, 37 → 22
   display lines. Notes are the dominant term in diagram height, so the estimated-height splitter
   (`AddNoteHeight` / `MaxEstimatedDiagramHeight`) will produce *fewer* diagram fragments for
   SQL-heavy scenarios. Width is still bounded by `skinparam wrapWidth 800` for anything containing
   spaces and by `WrapUnbreakableRuns` (120 chars) for anything that does not, so the 4 096 px
   PLANTUML_LIMIT_SIZE crop documented in `DIAGRAM_WIDTH_PLAN.md` is not newly reachable.
2. **The report's bytes change for every suite with a SQL, XML or text request body.** This is the
   point of the fix, but it moves the Kronikol4J report-output parity line again (§4).
3. **Copy-note-text becomes exact** for these bodies — no divider markup, no inserted breaks. A2/A3
   disappear as a consequence rather than needing their own fix.

### Not in scope, recorded

- The continuation line of a `wrapWidth` wrap loses its leading indentation (measured; it is how the
  engine lays out a wrapped note line). Part B reduces how often it happens; eliminating it would
  mean Kronikol doing its own pixel-aware wrapping, which is a much larger change.
- `maxUrlLength` chunking of arrow labels
  ([PlantUmlCreator.cs:290-291](src/Kronikol/PlantUml/PlantUmlCreator.cs#L290-L291)) is the same
  blind `ChunksUpTo`, but on a URL, where a mid-token break is the only option and is conventional.
  Leave it.

## 1.8 Tests (red first)

**Unit — `PlantUmlCreatorTests`** (new region "Non-form request bodies"):

1. `Multi_line_sql_request_body_keeps_its_own_line_breaks` — the ClickHouse fixture query; assert the
   note contains `subtractDays(t.transaction_period, 7)` whole and that no note line is exactly 80
   characters. **Red today.**
2. `Sql_ampersand_is_not_turned_into_a_divider` — `WHERE flags & 4 = 4`; assert
   `DoesNotContain("lightgray\">&")` and that the bitwise `&` survives. **Red today.**
3. `Xml_request_body_is_not_chunked` — SOAP fixture; assert `<soap:Body>` appears on one line
   (escaped form) and `&amp;` is intact. **Red today.**
4. `Long_form_url_encoded_segment_is_chunked` — the existing pin at
   [PlantUmlCreatorTests.cs:1504-1516](tests/Kronikol.Tests/PlantUml/PlantUmlCreatorTests.cs#L1504-L1516)
   must stay green (`key=vvv…` has no whitespace, so it is still classified as a form body).
5. `Form_body_divider_still_separates_pairs` — `a=1&b=2`; the grey divider survives.
6. Predicate matrix (`LooksLikeFormUrlEncoded`): single-line `k=v&k=v` → true; anything with `\n`,
   `\r`, a space or a tab → false; no `=` → false; empty/null → false; a single-line SQL statement
   (`SELECT 1 FROM t WHERE a = 1`) → false *because of the spaces*, which is the case that matters.
7. `Request_and_response_notes_agree_for_the_same_text_body` — the same multi-line body as request
   and as response produces the same note payload.

**E2E — extend the existing SQL-note fixture.** `ReportTestHelper.GenerateReportWithJsonYamlNotes`
already emits a plain-text SQL request note (note 0). Add a realistic multi-line ClickHouse query to
it (or a sibling fixture) and add one Playwright fact that reads the painted `<text>` runs and
asserts no SQL identifier is split across two rendered lines — the same "assert off the painted SVG"
discipline 3.0.81 used. Remember the one-`<text>`-per-word gotcha: group by `y`, sort by `x`, join.

**Copy path.** One fact that `Copy note text` on the SQL note yields text containing neither
`lightgray` nor a break inside `transaction_period`.

---

# Part B — the feature: a full-width note control

> "expand and contract the width of the note/box so that it is 'full width', in a similar way to the
> way you extend it vertically. It would of course adjust the line breaks accordingly."

## 2.1 What PlantUML can and cannot do — measured on the pinned engine

Every row rendered through `tools/render-bench/render-svg.js` against
`core-1.2026.8beta1-0e4f452.js`, one 224-character note line, baseline `skinparam wrapWidth 800`:

| construct | effect on note width |
|---|---|
| `skinparam wrapWidth N` | **works**, whole diagram (800 → 2 lines; 2000 → 1 line, svg 1 510 px) |
| no `wrapWidth` at all | no wrapping; note draws at natural width |
| `<style> note { MaximumWidth 1600 }` | **ignored** (teoz and non-teoz alike) |
| `<style> note<<wideNote>> { MaximumWidth 1600 }` | **ignored** |
| `<style> sequenceDiagram { note { MaximumWidth 1600 } }` | **ignored** (scope wrapper kills the selector) |
| `<style> .wideNote { MaximumWidth 1600 }` + `<<wideNote>>` on the note | **works, per note** |
| `skinparam noteWrapWidth N` | no effect |
| `skinparam maxMessageSize N` | no effect on notes, and does **not** constrain message labels once `wrapWidth` is raised |
| stereotype with no matching style block | no effect (control: `<<wideNote>>` alone changes nothing) |
| `.wideNote { MaximumWidth 300 }` (below `wrapWidth`) | **works** — note narrows to 398 px, wrapping at ~41 chars |
| `.wideNote { MaximumWidth 100000 }` | no wrap at all; the note draws at its natural width |
| `.wideNote { MaximumWidth 600 }` on a 400-char unbroken run | **overflows to 2 687 px** — `MaximumWidth` breaks at spaces only, exactly like `wrapWidth` |
| `.kronMono { FontName "Courier New" }` | **works, per note** — that note's text is `font-family="Courier New"`, everything else stays `sans-serif` |

Consequences of the last three:

- The control is genuinely **bidirectional**: "contract" can mean narrower than today's 800, not only
  "back to default".
- `MaximumWidth` is not a bound. A note's floor is its longest unbreakable run, and nothing the
  feature does can cap a note's width — `WrapUnbreakableRuns` (120 chars, applied at generation) stays
  the only real defence, and a clamp must be applied to the *value we choose*, not relied on from the
  engine.
- §2.9 below: the font finding changes what this feature is worth.

### It works on real Java PlantUML too

Measured through the IKVM renderer (`tests/Kronikol.Tests.PlantUml.Ikvm`), same three sources:

| | drawn width | note text rows | fonts |
|---|---|---|---|
| baseline (`wrapWidth 800`) | 866 px | 2 | sans-serif |
| `.kronWide { MaximumWidth 1600 }` | 1 506 px | 1 | sans-serif |
| `.kronMono { FontName "Courier New" }` | 922 px | 3 | sans-serif + **Courier New** |

This closes Q9 and cuts two ways:

- **Good, but worth less than it looks now that `Server` is being phased out:** a generation-time
  width or font option is portable, so it would reach `Local` and `NodeJs` users as well as
  `BrowserJs`. That is a smaller audience than it was, and it is no longer a reason on its own to
  build one (§2.6).
- **A narrow caveat, not a constraint:** because Java honours `MaximumWidth`, a widened source that a
  reader copies out and rasterises to PNG can cross `PLANTUML_LIMIT_SIZE`. Measured in §2.4: that
  limit is **PNG-only** (SVG drew 24 185 px uncropped), and the report's own engine runs at
  `maxSvgSize: 98304`. See §2.4 for what the cap is actually for.

Two findings worth carrying:

- **`.class { MaximumWidth }` is the only per-note width handle.** The element selector (`note { … }`)
  does not carry `MaximumWidth`; the class selector does. This is exactly the `.className { … }` form
  Kronikol already emits for `.eventNote`, `.assertionNote` and `.stepBody`
  ([PlantUmlCreator.cs:602-644](src/Kronikol/PlantUml/PlantUmlCreator.cs#L602-L644)).
- **It composes with the existing note classes and with multiple stereotypes.** Measured:
  `note<<eventNote>><<kronNoteWide>> right` in a diagram that also has a plain `<<eventNote>>` note —
  both keep `#CFECF7` and `font-size 11`, only the classed one widens (224 chars on one line vs a
  wrap at 800 px). Both stereotype positions work (`note<<a>><<b>> right` and `note right <<a>><<b>>`).
  Multi-stereotype notes are already in Kronikol's vocabulary (`hnote across <<stepDelimiter>><<stepBody>>`).

Also worth recording, because it contradicts `DIAGRAM_WIDTH_PLAN.md`: **in the JS engine
`wrapWidth` *does* wrap message/arrow labels** (a 159-character label wrapped at 800 and stayed whole
at 3 000). That plan's table was measured against the Java jar. Any width work that touches
`wrapWidth` must be measured on both engines, not assumed.

## 2.2 Therefore: per-note, not per-diagram

The honest design the measurement supports:

- the toggle lives **on the note**, like the vertical expand/contract;
- turning it on adds a `<<kronNoteWide>>` stereotype to that note's header line and injects
  `<style> .kronNoteWide { MaximumWidth <px> } </style>` into the re-render source;
- the diagram's `skinparam wrapWidth 800` is left alone, so every other note, every arrow label and
  the step bars keep today's geometry.

Nothing is emitted at generation time — the class and the style block exist only in the client-side
re-render source, the same way the YAML view's spliced lines do.

## 2.3 Where it plugs in

The per-note state machinery already exists and the width is one more dimension of it:

| concern | existing | addition |
|---|---|---|
| per-note state | `container._noteSteps[i]`, `container._noteFormats[i]` | `container._noteWidths[i]` (`'default'` \| `'full'`) |
| state setter + rollback | `setNoteState`, `setNoteFormat` → `rerenderWithNoteStates` ([collapsible-notes-script.js:1411-1436](src/Kronikol/Reports/collapsible-notes-script.js#L1411-L1436)) | `setNoteWidth`, same shape |
| source rebuild | `buildSourceWithNoteStates` ([:1342](src/Kronikol/Reports/collapsible-notes-script.js#L1342)) | stamp the stereotype on the note header as it is re-emitted; inject the style block once per source |
| note buttons | `createNoteButtons` ([:225](src/Kronikol/Reports/collapsible-notes-script.js#L225)) | one more top-right glyph |
| bulk | `buildNoteFormatQueue` + `window._setNoteFormat` / `_setScenarioNoteFormat` ([:2499-2542](src/Kronikol/Reports/collapsible-notes-script.js#L2499-L2542)) | `buildNoteWidthQueue` + `window._setNoteWidth` / `_setScenarioNoteWidth` |
| seeded default | `window._noteFormatDefault = '__NOTE_FORMAT_DEFAULT__'` ([:1678](src/Kronikol/Reports/collapsible-notes-script.js#L1678)) | `window._noteWidthDefault = '__NOTE_WIDTH_DEFAULT__'` |

### The regex hazard

Four matchers assume **at most one** stereotype on a note header and must be widened to
`(?:<<[^>]*>>)*` in the same commit:

- [collapsible-notes-script.js:11](src/Kronikol/Reports/collapsible-notes-script.js#L11) `parseNoteBlocks`
- [collapsible-notes-script.js:1229](src/Kronikol/Reports/collapsible-notes-script.js#L1229) `applyNoteFormats`
- [collapsible-notes-script.js:1355](src/Kronikol/Reports/collapsible-notes-script.js#L1355) `buildSourceWithNoteStates`
- [collapsible-notes-script.js:1742-1744](src/Kronikol/Reports/collapsible-notes-script.js#L1742-L1744) `isPositionalNoteStart` (inside `stripDatabaseCalls`)

The last one is the live one: the databases filter runs on the **rebuilt** source, so it sees the
stamped header. The first three run on `_noteOriginalSource`, but they must agree or a later change
will introduce a silent index skew between the note blocks and the rendered note groups — the exact
failure class `NoteButtonIndexTests` exists to catch.

**Do not** widen the search-index note opener
([report-search-index.js:40](src/Kronikol/Reports/report-search-index.js#L40) and its C# twin
[SearchNormalizer.cs:149-165](src/Kronikol/Reports/SearchIndex/SearchNormalizer.cs#L149-L165)). The
index is built at generation time from the generated source, which never carries the width class;
those two are a pinned cross-language vector with a Kronikol4J third copy and must stay byte-aligned.

## 2.4 Choosing the width

`.plantuml-browser svg { max-width: 100%; height: auto }`
([inline-svg-styles.css:1-5](src/Kronikol/Reports/inline-svg-styles.css#L1-L5)) means a diagram wider
than its container is **scaled down**, not scrolled. So "make it wider" is only a readability win if
the result still fits: past the container, wider text is smaller text.

"Full width" therefore means *fill the container exactly*. **Do not compute it from the note's x
offset** — that was the first draft of this section and it is wrong for the notes that matter most.

Measured: Kronikol emits `note left` for a **request** payload (the SQL note) and `note right` for a
response. A left note is anchored at x = 0 and widening it **pushes the participants right** rather
than extending the canvas: same source, `MaximumWidth 1500` applied, participants move from x = 751
to x = 1 077 and the SVG from 887 px to 1 212 px. So `noteBBox.x` is not stable across the change,
and any arithmetic that subtracts it is measuring the wrong thing.

The formula that holds for both sides needs no offset at all — it works from the *slack*:

```
targetMaximumWidth = clamp(
    currentNoteWidth + (containerInnerPx - drawnSvgWidth),   // give the note the slack, or take it back
    MIN_NOTE_WIDTH_PX,          // 320; below this a note is unreadable whatever the container
    MAX_NOTE_WIDTH_PX)          // 3600, see below
```

`currentNoteWidth` is `getNoteBBox(grp).width`
([collapsible-notes-script.js:188](src/Kronikol/Reports/collapsible-notes-script.js#L188)), already
used for button placement; `drawnSvgWidth` is the SVG's *natural* width, which the zoom code already
measures by clearing `max-width` and reading the bounding rect
([context-menu-script.js:743-756](src/Kronikol/Reports/context-menu-script.js#L743-L756)) — reuse
that rather than writing a second measurer. SVG user units are CSS px at scale 1. `containerInnerPx`
is `container.clientWidth` minus the 1 em `padding-left`; when the container is hidden (a collapsed
`details`) it is 0, so fall back to the report body width and recompute on the next render.

The relation is not exactly linear — widening a left note also moves the arrow labels that sit over
the shifted lifelines — so **one** correction pass after the re-render (rescale by
`containerPx / drawnPx`, capped at a single retry) is worth its cost and no more.

**Several notes widened at once is not a second problem.** `MaximumWidth` is a *maximum*: one class
per diagram, set from the widest note's slack, leaves narrower notes exactly where they were. Only
the widest note is at the target, which is the one the reader is looking at.

**Window resize is deliberately not tracked.** The target is computed at toggle time; re-rendering
every widened diagram on a resize is far too expensive for what it buys. A later toggle recomputes.

`MAX_NOTE_WIDTH_PX` is a **courtesy bound, not a correctness requirement** — the first draft of this
section overstated it. Measured, on the same source, through the IKVM renderer (real Java PlantUML,
no limit set, so the jar default is in force):

| output | drawn |
|---|---|
| SVG | **24 185 px — no crop at all** |
| PNG | **4 096 × 129 — cropped exactly at the limit** |

So `PLANTUML_LIMIT_SIZE` is a **raster** limit. It does not touch SVG, and it never touches the
report: the shipped browser engine is configured `{ maxSvgSize: 98304 }` at all three ESM call sites
([TrackingDefaults.cs:20](src/Kronikol/Constants/TrackingDefaults.cs#L20),
[plantuml-render.js:320](src/Kronikol/PlantUml/plantuml-render.js#L320),
[plantuml-browser-render-script.js:279](src/Kronikol/Reports/plantuml-browser-render-script.js#L279),
[plantuml-worker-host.js:200](src/Kronikol/Reports/plantuml-worker-host.js#L200)), twenty-four times
the raster limit. Part B's widened source also **never leaves the browser** — it lives in
`data-plantuml` and is rendered only by that engine.

The 4 096 px path is therefore narrow and human-in-the-loop: someone uses *Copy PlantUML source*
(the context menu's "Open source in new tab" opens a `text/plain` Blob — it does not render
anything), pastes it into plantuml.com or a local install, and asks for **PNG**. That is exactly how
the 3.0.83 component-diagram defect was reported, so it is a real path — but it is a degraded
experience for a copied source, not a broken feature.

**Recommendation: keep a cap, set it generously (≈4 000 px of note), and do not contort the design
for it.** A note wider than a 4K monitor is not a thing anyone asked for, so the cap will almost
never bind; its job is to stop a pathological container measurement, not to protect the raster path.

## 2.5 UI surfaces

**Per note.** One more glyph in the top-right hover cluster, to the left of the format button:
`↔` when the note is at default width, `→←` when it is full width. It appears only when widening
would change anything — i.e. the note's natural width exceeds the current wrap. That test is cheap
and exact: after a render, a note whose lines all fit in one row needs no button. (Mirror the lazy
eligibility pattern `_noteShowButtons` already uses for the format button: computed on first hover,
cached on the container.)

**Per scenario and per report.** A two-option select beside the note-format select, built once and
reused across the five toolbar variants via `BuildScenarioDiagramToolbar`
([ReportGenerator.cs:700-718](src/Kronikol/Reports/ReportGenerator.cs#L700-L718)) — the same
"built once so the variants cannot drift" contract, and the sixth hidden toolbar shape the 3.0.80
work found still applies. Gate it on `isPlantUmlBrowser` and on the report containing at least one
note that is wider than the wrap (the `hasJsonNotePayloads` pattern).

**Not in the URL hash.** Note state (step, format) is not in the hash today; width joins them.

## 2.6 Options and configured defaults

Two separate knobs, both optional:

1. **`ReportToggleDefaults.NoteWidth`** (`NoteWidthMode?` — `Default` \| `Full`), resolved through
   `ReportToggleDefaultsResolver` like every other setting, seeded into the script as
   `__NOTE_WIDTH_DEFAULT__`, and inert for non-`BrowserJs` reports. Recommended built-in: `Default`
   (a full-width start state changes every diagram's geometry on first paint, which is not something
   to impose).
2. **`ReportConfigurationOptions.DiagramNoteWrapWidth`** (`int`, default 800) replacing the hard-coded
   `MaxLineWidth` at [PlantUmlCreator.cs:20](src/Kronikol/PlantUml/PlantUmlCreator.cs#L20). This is
   the one that helps `Server` / `Local` / `Ikvm` users, who get no client-side control at all. It
   must be validated against 4 096 and documented as such.

   `.class { MaximumWidth }` and `.class { FontName }` are measured to work on **real Java PlantUML**
   as well as the JS build (§2.1), so a generation-time note class *could* give every renderer the
   per-note behaviour. With `PlantUmlRendering.Server` being phased out that argument now covers only
   `Local` and `NodeJs`, which is not enough to justify the extra surface on its own — **recommend
   keeping (2) a simple width option and putting the per-note behaviour entirely in the client**.
   Name it for the *note* rather than for `wrapWidth` so it need not be renamed if that changes.

   One relationship to keep: `MaxNoteChunkChars = 80` exists because it keeps a form-body line under
   `MaxLineWidth` so a wrap never splits an inline colour tag
   ([PlantUmlCreator.cs:21](src/Kronikol/PlantUml/PlantUmlCreator.cs#L21)). Making the width
   configurable breaks that comment's arithmetic for any value below ~720 px; either clamp the option
   or derive the chunk size from it.

(2) is the lower-risk half.

## 2.7 Interactions to get right

| interaction | what must happen |
|---|---|
| fragmented diagrams (`.puml-fragment`) | **already safe, with one constraint.** `parseDiagramStructure` treats `<style>` blocks as part of the prefix it replicates into every fragment ([plantuml-browser-render-script.js:460-468](src/Kronikol/Reports/plantuml-browser-render-script.js#L460-L468)) — but it matches `trimmed === '<style>'` and `trimmed === '</style>'`, so the injected block must put those tags on their own lines, exactly as `AddEventStyling` already does. Width also changes line counts and therefore the height-based split; the per-note index must survive it — `_computeGlobalNoteIndex` is the shared arithmetic, do not fork it (3.0.67). |
| non-`BrowserJs` interactive diagrams | `InlineSvgRendering` containers get the context menu and note detection but have no `window.plantuml` to re-render with. The button must be gated on `isPlantUmlBrowser`, not on `hasInteractiveDiagrams`. |
| `TOOLBAR_REDESIGN_PLAN.md` | that plan's decisions are locked around container-query thresholds (1350 / 876 / 520 / 430) and a mandatory complementary-drop rule for control pairs. A new scenario-level select changes what fits at each threshold. Whichever of the two ships second must re-run that plan's `sweep` / `baselineaudit` / `heightprobe` gates. |
| zoom controls | a widened diagram that now exactly fits should *lose* its zoom controls; they are added lazily by an IntersectionObserver on width, so re-evaluate after the render. |
| Copy / Open PlantUML source | the copied source carries the class and the style block (consistent with note states). The 3 600 px cap is what keeps it renderable on plantuml.com. |
| Export Filtered HTML | the exported file must carry the seeded default and the new script, exactly as `FilterModeDefaultsTests` pins for the filter modes. |
| databases / steps / assertions filters | all run on the rebuilt source — see the regex hazard in §2.3. |
| component diagram panel | `isComponentDiagramContainer` must exclude it, as it does for every other note control. |
| merged reports | `MergeableReportRenderer` forwards toggle defaults; add `NoteWidth` to that forwarding. |

## 2.8 Tests

**Engine contract (guards the whole feature).** A Playwright fact that renders a two-note diagram
where one note carries `<<kronNoteWide>>` and asserts: the classed note draws its long line on one
row, the unclassed one wraps, and both keep their `<<eventNote>>` fill. If a future engine bump
breaks `.class { MaximumWidth }`, this is the test that says so.

**Unit.** `ReportToggleDefaultsResolverTests` chain/independence facts for `NoteWidth`;
`ToggleDefaultsMarkupTests` for the seeded token and the select markup;
`ToggleDefaultsBaselineTests` must still prove byte-identity for `null` vs `BuiltIn` vs
`Resolve(empty)` — a new setting whose built-in is `Default` must not move a single byte of an
existing report.

**E2E.** Per-note toggle round trip (widen → note draws wider, source carries the class → contract →
byte-identical to the pre-toggle source); scenario-level and report-level bulk; width survives a
headers-hide, a truncate change and a JSON⇄YAML flip; width state survives a fragment re-split;
`↔` is absent on a note that already fits. `PollingInterval = 200` on every `WaitForFunctionAsync`,
`.First`/`.Nth` on every possibly-multiple selector, and `dispatchEvent` for the SVG hover — per
`CLAUDE.md`.

## 2.9 The bigger readability lever: notes are drawn in a proportional font

This is the finding that most changes what Part B is worth, and it was not in the first draft.

**PlantUML draws note text in `sans-serif`** (measured: every `<text>` in a note carries
`font-family="sans-serif"`). Analytics SQL — the payload that prompted this whole plan — is written
with its `AS` clauses padded into columns, and a proportional font destroys that alignment no matter
how wide the note is.

Measured on five note lines whose `AS` token sits in the **same source column**:

| | x of each `AS` | spread |
|---|---|---|
| `sans-serif` (today) | 247.4, 236.6, 253.2, 240.2, 302.4 | **65.8 px** |
| `.kronMono { FontName "Courier New" }` | 388.7, 388.7, 388.7, 388.7 (+ one genuinely at a different column) | **0 px** for same-column text |

So a reader looking at the user's query today sees a ragged 66-pixel scatter where the author wrote a
straight column — and widening the note does not improve that by one pixel. Indentation of nested
`CASE`/`WHEN` blocks suffers the same way.

`FontName` rides on **exactly the same mechanism** as `MaximumWidth`: a `.class` style block plus a
stereotype on the note header, per note, on both engines (§2.1). Every piece of machinery Part B
builds — the per-note state, the source stamping, the style injection, the buttons, the bulk queues,
the seeded default — serves both properties with no additional structure; only the value written into
the style block differs.

**Recommendation: ship monospace as a sibling of width, not as a later idea.** For the stated goal
("we want things like sql queries easy to read") a monospace toggle is plausibly the *larger* half of
the win and is strictly cheaper, because it needs none of the container measurement, none of the
correction pass, and none of the 4 096 px clamp. If only one of the two ships, it should be this one.

Open sub-questions, none blocking:

- Which font to name. `Courier New` is the safe cross-platform choice; PlantUML also accepts
  `monospace`, which defers to the viewer. Worth one measurement of each on Windows/macOS/Linux
  Chromium before choosing — a font PlantUML cannot resolve falls back silently.
- Monospace is wider per character, so a note that fits today may wrap once it is monospaced. The two
  features compose (both are properties of the same class) but the measurement order matters: apply
  the font first, then compute the width target from the re-rendered note.
- Whether monospace should be the *default* for notes whose payload is SQL. Tempting, and it would
  fix the reported report with no interaction at all — but Kronikol does not know a note is SQL at
  render time (the note is plain text by then), and making every note monospace changes every
  existing report's bytes and geometry. Recommend: a toggle and a configured default, not automatic.

## 2.10 All six open measurements — now taken

Every item that was listed here as unmeasured has been measured. None of them blocks Part B; two
change the design and one is a pre-existing defect in an unrelated feature.

### 1. Bulk re-render cost — not a veto

The report-wide note-format dropdown is the same shape as a report-wide width control (rebuild every
source, re-render every diagram). On a 12-scenario `LargeReportFixture` report — **110 containers,
101 drawn SVGs** — measured in headless Chromium with the render queue's own idle signal:

| | wall time | worst main-thread task |
|---|---|---|
| report-wide -> YAML | 4 683 ms | 196 ms |
| report-wide -> JSON (back) | 3 970 ms | 223 ms |

About 40 ms per container, and the main thread never blocks for more than ~220 ms because the renders
run in workers. Extrapolated to the reported 488-diagram production report that is ~20 s of
background work — slow, but exactly the cost the note-format dropdown already charges and ships with.

**Decision: the report-level control is allowed**, provided it uses the same `renderWithPending`
pending state the format dropdown does. No new budget is needed.

### 2. Which font name to use — `Courier New`, and only `Courier New`

This turns out to matter much more than expected, because the **engine sizes the note box and the
browser paints the text**, and they resolve font names independently. Measured both sides on the same
string (engine = drawn SVG width on the pinned build; browser = canvas advance width in Chromium):

| `FontName` | engine width | browser advance | verdict |
|---|---|---|---|
| *(none — sans baseline)* | 665 px | 221.7 | baseline |
| **`Courier New`** | **765 px** | **280.8** | **both resolve it; the only name that agrees** |
| `monospace` | 708 px | 221.7 | engine uses a generic mono metric, browser paints **sans** |
| `Consolas` | 708 px | 257.3 | engine's generic metric ≠ Consolas' real metric |
| `DejaVu Sans Mono` | 615 px | 221.7 | neither resolves it |
| `Menlo` | 615 px | 221.7 | neither resolves it (on Windows) |
| `Nonexistent Font XYZ` | 615 px | 221.7 | the unknown-font metric |

Two things fall out:

- **An unresolved font is not a silent no-op.** The engine's unknown-font metric (615 px) is not the
  sans metric (665 px), so a name it cannot resolve still resizes the note — with metrics matching
  neither the request nor what the browser will paint. Every mismatch row above sizes a box for one
  font and paints another into it.
- **`document.fonts.check()` is useless as a guard** — it returned `true` for every name above,
  including `Nonexistent Font XYZ`. A real check must compare advance widths, or assert the painted
  text stays inside the note's path bbox. The latter is the E2E guard to write.

The engine's metrics are platform-independent (TeaVM-compiled), so only the browser half varies;
`Courier New` ships on Windows and macOS and is fontconfig-aliased to Liberation Mono on mainstream
Linux. Residual risk is a Linux box with no alias, which the bbox-containment guard would catch.

### 3. Truncated and collapsed notes — no interaction

Established from the code rather than measured, because the code is unambiguous:
`buildSourceWithNoteStates` counts **source** lines (`truncateLineCount++` per line of the note
block), and `isLongNote` counts source content lines too. Width changes how many *display rows* a
source line occupies and never which source lines are emitted, so truncation, collapse and the
long-note test are all width-independent. The only knock-on is visual height, which feeds the
height-based fragment split — already covered in §2.7.

### 4. Button eligibility — painted rows vs source lines is exact

Measured on a note that wraps and two that do not, reading `_parseNoteBlocks` against
`_findNoteGroups` on the live page:

| note | source lines | painted rows |
|---|---|---|
| one 300-char line among short ones | 3 | **7** |
| the same note plus 2 grey header lines and 2 blank lines | 7 | **11** |
| 14-line SQL, longest line 122 chars | 14 | 14 |
| 5-line JSON response | 5 | 5 |

`painted > source` is an exact wrapping signal, and it survives both shapes that could have broken
it: blank lines each paint a distinct whitespace-only `<text>` row (as recorded for 3.0.81), and
grey header lines paint one row each. Compare against the note block parsed from the **current**
`data-plantuml`, not the original source, so a headers-hidden note compares like with like.

### 5. Zoom — an intermediate zoom level is discarded on every note re-render

Measured: zoom a diagram to 74 % (slider min 47), then force a note re-render. Afterwards the slider
reads **100** with min 79 and the SVG's inline width is cleared.

The cause is structural, not a Part B problem: the render lands via `el.innerHTML = svg`, which
destroys the prepended `.diagram-zoom-controls`; the six `_addZoomButton` calls in
`collapsible-notes-script.js` rebuild them, and `addZoomButton` seeds `slider.value` from the
`diagram-natural-size` class alone — the only zoom state that is modelled. `restoreZoomState`
restores that same binary and nothing else.

**This is pre-existing and already reachable today** through expand/collapse, the headers toggle and
the JSON⇄YAML dropdown — not something the width control would introduce. It is arguably working as
designed (the class *is* the persisted state), so it is recorded here as a finding rather than
folded into Part B: persisting the percentage would be a small, separate change to
`restoreZoomState` plus a `container._zoomPct`.

One thing that is **not** broken, contrary to the first reading of the code: a diagram that only
becomes too wide after a re-render does still get zoom controls, because the re-render paths call
`_addZoomButton` again rather than relying on the one-shot IntersectionObserver.

### 6. Print — no print stylesheet exists at all

`stylesheets.css` has no `@media print` block. A widened note therefore prints under the same
`max-width: 100%` rule as everything else: it fits the page, scaled down, so a note sized for a
1600 px screen container prints smaller than one sized for 800 px. A degradation rather than a break,
and equally true of any wide diagram today. Worth one `@media print` rule if the feature ships, not
worth blocking on.

## 2.11 An alternative deliberately not taken

A note's text could be shown in an HTML panel instead — the context menu already has *Copy box text*,
so *Open box text* beside it would cost little and would sidestep every PlantUML constraint at once:
real monospace, real text selection, real horizontal scroll, no re-render, no width arithmetic, no
4 096 px crop.

It is not the ask ("expand and contract the width of the note/box … in a similar way to the way you
extend it vertically" is plainly about the note in the diagram), and it splits the reading experience
in two. Recorded because it is the cheapest path to "SQL that is easy to read" by a wide margin, and
because if Part B ever stalls on the measurement machinery this is the fallback that still delivers
the user's goal.

---

# 3. Milestones

| # | scope | ships |
|---|---|---|
| ~~**M1**~~ | ~~Part A: `LooksLikeFormUrlEncoded` + the branch change; unit facts 1-7; E2E painted-SVG fact; copy-text fact~~ | **shipped 3.0.84** |
| | *(every milestone below shipped in 3.0.85)* | |
| **M2** | Part A ride-along: `DiagramNoteWrapWidth` option (§2.6 item 2) + its 4 096 validation + wiki row | same release |
| **M3** | Part B foundations: the engine contract test, the four regexes, `_noteWidths` state, source stamping + style injection, `setNoteWidth` | next release |
| **M4** | Part B UI: note button (with eligibility), scenario + report selects via `BuildScenarioDiagramToolbar`, bulk queues | same |
| **M5** | Part B defaults: `ReportToggleDefaults.NoteWidth`, resolver, seeded token, merge forwarding, baseline byte-identity pin | same |
| **M6** | Part B interactions: width measurement + one refinement pass, zoom re-evaluation, fragment re-split, Export Filtered HTML | same |
| **M7** | Docs, changelog, Kronikol4J ledger, version bump across all packages, tag, push | both |

M1 shipped on its own as 3.0.84. The §2.10 measurements are all taken and none blocks M3; the one
design change they force is §2.9 + Q13 — **M4/M5 should carry the font toggle first and the width
control second**, because the font is the larger measured readability win, needs none of the
container arithmetic, and is the half that can ship without a clamp.

# 4. Docs, changelog, Java port

- **Wiki `Content-Formatting.md`** — the pipeline section currently says the library "formats" a
  non-JSON request body without saying what that means. State the new contract: a request body that
  is not JSON and not form-url-encoded is shown as captured, with creole neutralised and unbreakable
  runs over 120 characters broken; only a single-line `k=v&k=v` body gets the `&` dividers.
- **Wiki `Generated-Reports.md` / `Report-Configuration.md`** — the width control, the
  BrowserJs-only marker, `DiagramNoteWrapWidth`, and the `NoteWidth` row in the toggle-defaults table.
- **`DIAGRAM_WIDTH_PLAN.md`** — add the JS-vs-Java `wrapWidth`-on-message-labels divergence found
  here; that plan's table is Java-only and now says so.
- **`PLANS_STATUS.md`** — register this plan.
- **Kronikol4J** — Part A changes report *output* bytes for every non-JSON request body, so the
  standing divergence ledger in its README gains an entry. Part B is client-side script only (the
  port is pinned to 3.0.43 assets), so it extends the existing `collapsible-notes-script.js`
  divergence rather than opening a new class.
- **CHANGELOG** — the bug entry should name the symptom the user will recognise ("SQL and XML
  request bodies were broken mid-word every 80 characters") and say that report bytes change.

# 5. Open questions, with recommendations

| # | question | recommendation |
|---|---|---|
| Q1 | Keep any chunking for non-form request bodies? | **No.** `WrapUnbreakableRuns` (120, line-aware, punctuation-preferring) plus `wrapWidth` already cover both failure modes it was standing in for. |
| Q2 | Predicate for "is form-url-encoded"? | `=` present, and no `\n`/`\r`/space/tab. A misclassification is harmless in one direction (a form body with a raw space simply is not chunked) and impossible in the other (no text body of interest is whitespace-free). |
| Q3 | Keep the `&` divider at all? | **Yes**, for true form bodies — it is what makes them readable. It was never meant to run on anything else. |
| Q4 | Width states: two or three? | **Two** (Default ⇄ Full width). Narrowing is measured to work, so a third state *could* be a genuine "narrow", but a third "natural, no wrap" state only matters when natural width exceeds the container, where it forces the diagram to scale down — worse, not better. |
| Q5 | Per-note or per-diagram? | **Per note**, now that `.class { MaximumWidth }` is measured to work. Per-diagram would have been the fallback and is strictly worse: it widens arrow labels too. |
| Q6 | Does the copied PlantUML source carry the width? | **Yes.** The 4 096 px limit turns out to be PNG-only and irrelevant to the report (its engine runs at `maxSvgSize: 98304`), so the cap is a generous sanity bound, not a design constraint — §2.4. |
| Q7 | Width in the URL hash? | **No** — no note state is. |
| Q8 | Built-in default for `NoteWidth`? | **`Default`.** Byte-identity for existing reports is the stronger constraint. |
| Q9 | Does `.class { MaximumWidth }` work on the Java/IKVM engine? | **Measured: yes** (866 → 1 506 px), and so does `FontName`. Closed. |
| Q10 | Should `hnote across` step bars get the same control? | Out of scope. They span the diagram already and their width is governed by the coloured-bar crash cap. |
| Q11 | Monospace note text — same feature or separate? | **Same feature, and arguably the more valuable half** (§2.9). Identical mechanism, no measurement machinery, and it fixes an alignment loss that width cannot touch. If only one ships, ship this. |
| Q12 | Does the width control need to survive a window resize? | **No.** Computed at toggle time; re-rendering every widened diagram on resize costs far more than it returns. |
| Q13 | Which monospace font name? | **`Courier New`** — measured (§2.10.2) as the only name the engine and the browser both resolve. Anything else sizes the box with one font's metrics and paints another into it. |
| Q14 | Is the report-level bulk control affordable? | **Yes** — 4.7 s for 110 containers, worst main-thread task 223 ms (§2.10.1), the same cost the note-format dropdown already ships. Must reuse `renderWithPending`. |
| Q15 | Should Part B also persist the zoom percentage across a re-render? | **No — separate change.** §2.10.5 shows an intermediate zoom is discarded today by expand/collapse and the YAML dropdown alike. Pre-existing, arguably by design, and mixing it into Part B would hide it. |

---

# Appendix — measurement log

Engine: `tools/render-bench/core-1.2026.8beta1-0e4f452.js` (the shipped pin). Renders via
`node tools/render-bench/render-svg.js <engine> <file.puml>`, run from `tools/render-bench` (the page
shell serves `viz-global.js` and the engine from that directory).

Reading a rendered note back: PlantUML emits **one `<text>` element per word**, so group by `y`,
sort by `x`, and join — the same gotcha recorded for the 3.0.83 E2E work.

```js
// dump.js — rendered SVG -> one line of text per drawn row
const s = require('fs').readFileSync(process.argv[2], 'utf8');
const rows = new Map();
for (const m of s.matchAll(/<text\b([^>]*)>([\s\S]*?)<\/text>/g)) {
  const x = parseFloat(/\bx="([-\d.]+)"/.exec(m[1])[1]);
  const y = parseFloat(/\by="([-\d.]+)"/.exec(m[1])[1]);
  (rows.get(y) ?? rows.set(y, []).get(y)).push({ x, t: m[2] });
}
[...rows.keys()].sort((a, b) => a - b).forEach(y =>
  console.log(y, JSON.stringify(rows.get(y).sort((a, b) => a.x - b.x).map(p => p.t).join(''))));
```

Generator side: a throwaway `[Fact]` calling
`PlantUmlCreator.GetPlantUmlImageTagsPerTestId([log]).Single().PlantUmls.First().PlainText` with a
`RequestResponseLog` whose `Type` is `Request` and whose `Content` is the body under test reproduces
every §1.2 / §1.5 output directly. Note that `tests/Kronikol.Tests` does not currently compile —
three untracked red tests from the in-flight `LLM_FRIENDLY_PLAN` M0 work (`RunEndPointerTests`,
`ReportGeneratorDiagnosticsScopeTests`, `TestRunReportSchemaContractTests`) reference types that do
not exist yet; park them to run anything in that project.
