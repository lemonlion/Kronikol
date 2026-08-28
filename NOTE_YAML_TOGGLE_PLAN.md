# Note payload JSON ⇄ YAML hover toggle — implementation plan

> **Status: implemented and shipped in 3.0.59** (2026-08-26). All of v1 landed as designed:
> reconstructor + token-level YAML emitter + conservative re-escape + `_noteFormats` state +
> hover button, in `collapsible-notes-script.js`; internals driven via
> `window._noteFormatInternals` from `NoteYamlInternalsTests`, interaction matrix in
> `NoteYamlToggleTests`. The open questions (bulk toggle, default-format option) remain open
> as possible follow-ups; Kronikol4J adoption is recorded in that repo's README divergence
> note (its goldens pin 3.0.43 assets, so the byte-copy waits on a golden re-capture there).

This document is self-contained: it carries everything needed to implement the feature
without prior session context. Read "How the system works today" before touching code.

## Goal

A hover button on diagram note payloads that toggles the displayed body between JSON
and YAML. The payoff: YAML renders strings containing `\n` (SQL queries, stack traces,
scripts) as block scalars with their original line breaks and indentation. Today a
captured body like

```json
{
  "query": "SELECT o.id,\n       o.total\nFROM orders o\nWHERE o.status = 'open'"
}
```

displays as one soft-wrapped line with literal `\n` sequences. In YAML view it becomes:

```yaml
query: |-
  SELECT o.id,
         o.total
  FROM orders o
  WHERE o.status = 'open'
```

**Approach: entirely client-side.** No .NET changes, no report size cost, works on
already-generated reports once they use the updated script asset. The JSON is
recovered from the note text in the browser (provably reversible — see below),
converted to YAML on toggle, and gated by `JSON.parse` — a note whose text can't be
reconstructed into valid JSON simply never shows the button. Toggling back to JSON
needs no reverse conversion: the original note lines are restored from
`_noteOriginalSource`, exactly as expand/collapse does today.

## How the system works today

### Generation side (.NET) — read-only background, no changes needed here

`src/Kronikol/PlantUml/PlantUmlCreator.cs` builds each diagram note:

1. `FormatNoteContent` (~line 757) formats a captured body:
   - `TryFormatAsJson` pretty-prints valid JSON via
     `Utf8JsonWriter { Indented = true, Encoder = UnsafeRelaxedJsonEscaping }`.
     Newlines inside string values remain literal `\n` escapes on one line.
   - `TryFormatTruncatedJson` re-indents capture-capped JSON *prefixes* (not valid
     JSON; ends with a truncation marker).
   - GraphQL, form-urlencoded, plain-text and binary bodies take other paths.
2. `EscapeCreoleMarkup` (~line 381) neutralises PlantUML creole markup, per line:
   - Pair markers `//` `**` `__` `--` `""` and links `[[…]]` are only "live" when the
     line contains a partner (two doubled occurrences; for `[` a `]]` later in the
     line). A live doubled marker `XX` is emitted as `~X~X`.
   - A line's first non-space char `*`, `#` or `=` (bullet/heading) gets a `~` prefix
     unless it's already a live pair.
   - `<` gets a `~` prefix when followed by a tag-start char (`/`, `#`, or an ASCII
     letter — see `IsCreoleTagStart`).
3. `WrapUnbreakableRuns` (~line 973): any whitespace-free run over 120 chars
   (`MaxUnbrokenRunChars`) is broken with a **plain unmarked `\n`**, preferring a cut
   after one of `,;:}]"&=)/` in the last 24 chars, never inside a `<tag>`, never
   stranding a `~` from the char it protects.
4. `JsonFocusFormatter.FormatWithFocus` (only when `FocusFields` configured)
   interleaves `<b>`/`<color:…>` emphasis markup into the JSON text.
5. Gray header lines `<color:gray>[Header=Value]</color>` are prepended, then
   `EscapeForPlantUmlNote` (~line 365) doubles every backslash (`\` → `\\`) over the
   whole note, and the result is emitted between `note left`/`note right` … `end note`
   in the diagram source. Very long response notes may be **server-side chunked** into
   several notes bracketed by `..Continued From Previous Diagram..` /
   `..Continued On Next Diagram..` lines (`AppendResponseNoteContent`).

### Interaction side (browser) — where all the work happens

`src/Kronikol/Reports/collapsible-notes-script.js` (embedded into reports via
`DiagramContextMenu.GetCollapsibleNotesScript()` / `LoadResource`; the Kronikol4J port
ships the same asset). Key machinery, all in that one file:

- Diagram source reaches the page per container: `_pumlData[el.id]` (compressed map)
  or `data-plantuml` / `data-plantuml-z` attributes;
  `container._noteOriginalSource` holds the master copy (set in several places —
  grep `_noteOriginalSource =`).
- `parseNoteBlocks(source)` finds `note left|right … end note` blocks and returns
  `{start, end, contentLines}` per note, in source order.
- Per-note display state lives in `container._noteSteps[noteIdx]`:
  0=collapsed, 1=truncated, 2=expanded (3=truncated-on-the-way-down). Related state on
  the container: `_truncateLines`, `_headersHidden`, `_assertionsVisible`,
  `_stepsVisible`, `_databasesVisible`.
- `setNoteState(container, noteIdx, targetStep)` is the single mutation path: it
  rewrites the source via `buildSourceWithNoteStates(origSource, noteSteps,
  noteBlocks, hideHeaders, truncateLines)` (which walks the source and, per note,
  emits collapsed preview / first-N-lines truncation / full lines), applies the
  assertion/steps/databases filters, then re-renders through the browser PlantUML
  worker (with fragment re-splitting via `_splitWithChunkedNotes`, prefetch, and the
  `_svgCache`).
- `makeNotesCollapsible` matches SVG note shapes to source note blocks and calls
  `createNoteButtons(svg, bbox, noteStep, onExpand, onContract, onTruncate, onCycle,
  contentLines, grp, container, forceIsLong)` which builds the hover buttons
  (`+`/`−`/`▾`/`▴`) as SVG groups, shown/hidden by `mouseenter` handlers
  (`_noteShowButtons`, with repositioning for tall notes).
- Index alignment is the fragile part and is already solved: `filteredToOrigMap`
  (filters removed notes), `sourceIndexMap` (notes empty in the rendered SVG), and
  `fragContinuationMap` (client-side fragment splits) all translate an on-screen note
  back to its **original-source note index**. All new per-note state must be keyed by
  that original index, and content swapped inside `buildSourceWithNoteStates`, so the
  existing mapping applies for free.

A format toggle is structurally the same operation as expand/truncate: flip per-note
state → rebuild source → re-render.

## Recovering the JSON from the note text

Each generation-time transform is reversible or repairable, in this order:

| Step | Transform (generation) | Reversal (browser) |
|---|---|---|
| 1 | Gray headers prepended | Drop lines matching `^<color:gray>` (and the following blank line) — payload region is what remains. |
| 2 | Backslash doubling `\`→`\\` | Halve backslashes — uniform, lossless. |
| 3 | Focus markup `<b>`/`<color:…>` | Strip *unescaped* tags. Any real `<` in the payload was `~`-escaped, so unescaped tags are provably Kronikol's. |
| 4 | Creole escapes | `~X` → `X` when `X` is one of `/ * _ - " [ < # =` (pair chars, tag-start `<`, leading bullets). One narrow ambiguity: a payload *literal* `~` directly before a doubled live marker — see Risks. |
| 5 | `WrapUnbreakableRuns` breaks | JSON grammar forbids raw newlines inside string literals, so a line ending inside an unterminated string is provably a wrap break — join with the next line. Deterministic repair. |

**Validation gate:** `JSON.parse` the reconstructed text. Parses → note is eligible,
button shown. Doesn't parse → no button, no toggle. This automatically excludes
capture-truncated prefixes (they end in a truncation marker), GraphQL-formatted
bodies, form-urlencoded, plain text, the `<i>[binary content]</i>` placeholder,
server-side chunked continuation notes (each chunk alone isn't valid JSON), and any
repair failure. The failure mode is "toggle unavailable", never wrong output.

Reconstruction always runs on the **full original note lines** (from
`_noteOriginalSource` via `parseNoteBlocks`), never on the truncated/collapsed
rendering currently on screen.

**Emission is token-level, not from the parsed value.** `JSON.parse` is only the
gate; emit YAML by walking the reconstructed JSON text's tokens so three things a JS
object cannot represent pass through verbatim (the codebase's "show the bytes on the
wire" principle):

- int64s beyond 2^53 (`JSON.parse` silently rounds `9007199254740993`);
- duplicate keys (JS objects keep last-wins only);
- key order for integer-like keys (JS object iteration reorders `{"2":…, "1":…}`).

## Design

All in `collapsible-notes-script.js` (or a sibling embedded script if size warrants —
follow how `DiagramContextMenu` loads resources and where `ReportGenerator` inlines
them, ~lines 568–569 of `src/Kronikol/Reports/ReportGenerator.cs`).

1. **Reconstructor**: note contentLines → candidate JSON text (table above) →
   `JSON.parse` → eligible or not. Run lazily on the note's **first `mouseenter`**
   (inside the existing `_noteShowButtons` show handler), not eagerly in
   `createNoteButtons` — eligibility for hundreds of notes must not be computed on
   every diagram render. Cache the verdict (and, after first toggle, the emitted YAML
   lines) per original note index on the container.
2. **YAML emitter** (~150–200 lines, no library, fed by the token walk):
   - Objects → mappings, arrays → sequences, 2-space indent; number literals emitted
     exactly as they appear in the JSON text; `true`/`false`/`null` pass through.
   - String values containing `\n` → literal block scalar `|-` (`|` when the string
     ends with a newline; explicit indentation indicator `|2-` when the first line
     starts with a space). Content lines keep the payload's own leading whitespace —
     this is the SQL readability win.
   - **Quoted-style fallback** (double-quoted with escapes, no worse than today's JSON
     view) for strings a block scalar can't faithfully represent: containing `\r` or
     other control chars, lines with trailing whitespace (parsers may strip it), or
     any >120-char whitespace-free run (see item 3).
   - **Leading-newline strings unfold too (3.0.63 addendum):** the original eligibility
     rule rejected any string starting with `\n`, which silently kept every SQL body
     from a C# raw string / indented heredoc (they begin with a newline) in the quoted
     fallback. A block scalar represents a leading `\n` as empty line(s) opening the
     block; YAML anchors block indentation on the first NON-empty line, so that line —
     not `blockLines[0]` — now drives the `|2` indicator. Strings with no non-empty
     line at all (`"\n"`) stay quoted, and the indicator-in-sequence fallback is
     unchanged. Round-trip-verified through js-yaml.
   - **Backslash doubling removed (3.0.62 addendum):** probing plantuml.js 1.2026.6
     and the IKVM jar (with and without teoz) showed block notes render backslash
     sequences literally — the only consumed sequence is `\t` (always rendered as a
     real tab; the final `\t` pair of any backslash run is consumed, so doubling
     never protected it and merely displayed `\\n` for a wire `\n`). The generator
     now emits note payload bytes verbatim, the reconstructor no longer halves,
     and `escapeNoteLine` no longer doubles. Reconstruction became *more* exact:
     the halving's `\\`-ambiguity is gone.
   - **CRLF exception** (added after the fact — Windows-captured payloads all carry
     `\r\n`, so the `\r` rule above sent every real multiline string to the quoted
     fallback): when *every* line break in the string is exactly `\r\n` (no lone `\r`,
     no bare `\n`), display it as a block scalar with the `\r` dropped. The YAML view
     trades those bytes for readability; the JSON view stays exact. Mixed or lone `\r`
     still falls back to the exact quoted form.
   - Single-line strings quoted only when YAML requires it (leading/trailing space,
     special chars, empty, looks-like number/bool/null/date). Keys quoted/escaped when
     needed (empty, special chars, embedded newlines).
   - JSON `\uXXXX` escapes decode to their characters — deliberate readability choice,
     same spirit as `UnsafeRelaxedJsonEscaping` on the .NET side.
3. **Re-escape for render**: before splicing YAML lines into the render source, apply
   a *conservative* escape — unconditionally `~`-escape every creole pair marker and
   tag start, double backslashes, and wrap >120-char whitespace-free runs. ~15 lines;
   deliberately dumber than the C# escaper because the client-built source is
   transient and never read by humans. **Exception**: the run wrap must never fire
   inside block-scalar content — an inserted newline there *changes the string's
   meaning* in YAML (unlike JSON view, where wrap breaks are only visual). Strings
   with such runs take the quoted fallback (item 2), where a wrap is harmless again.
   This matters because the report's SVG copy-text feature (see
   `CopyHighlightedTextTests.cs`) means readers copy the YAML expecting fidelity.
4. **State**: `container._noteFormats[origNoteIdx]` (`'json'` default | `'yaml'`)
   beside `_noteSteps`. `buildSourceWithNoteStates` swaps the note's payload lines for
   the escaped YAML lines (gray header lines untouched) **before** its
   collapse/truncate logic — so truncation and `isLongNote` operate on the active
   format's line count (a SQL query unfolding to 30 YAML lines correctly becomes a
   "long" note). Toggling back = stop swapping; original lines come from
   `_noteOriginalSource` untouched. `getNotePreview` (collapsed one-liner) keeps using
   original JSON lines.
5. **Button**: new hover button in `createNoteButtons` (suggested glyph `Y` ⇄ `J`),
   top-right beside `−`, only for eligible notes when not collapsed. Click flips
   `_noteFormats[idx]` and reuses the `setNoteState` rebuild + re-render path (factor
   a shared helper rather than duplicating the fragment/render logic). The top-right
   cluster already positions `▴` relative to `−` and repositions for tall notes in
   `_noteShowButtons` — the new button joins that layout and show/hide wiring.
   Styling per `collapsible-notes-styles.css` so themes are covered.

### Exclusions (v1)

Notes with `FocusFields` emphasis: tags are strippable for reconstruction, but the
YAML view loses the emphasis — strip it in YAML view (simplest) or defer those notes
to a follow-up with a YAML-aware focus formatter.

### Open questions (decide during v1)

- **Bulk toggle**: a per-diagram "Show payloads as YAML" context-menu entry
  (`context-menu-script.js` / `DiagramContextMenu.cs`) would be consistent with the
  existing assertion/step/database toggles and nearly free once per-note machinery
  exists (set all eligible `_noteFormats`, one re-render).
- **Default format option**: a report-level "start notes in YAML" preference. Not
  needed for the core ask; a script-side default stays client-only, a
  generation-time default would touch .NET and Kronikol4J.
- **Collapsed preview** while in YAML mode: confirm original-JSON preview reads
  acceptably (button is hidden when collapsed).

## Client-side performance

- **Page load**: zero — nothing embedded, nothing runs at load; the script adds a few
  KB to report HTML.
- **Eligibility check**: lazy on first hover + cached → microseconds per hovered
  note; never a per-render sweep.
- **Toggle click**: dominated by the PlantUML re-render — the identical operation an
  expand/collapse click already performs (worker-threaded, cached, prefetched). Token
  walk + YAML emit is microseconds by comparison. YAML views with unfolded strings
  have more lines, so that render is marginally slower and may re-split fragments —
  same behavior as expanding a long note today.
- **Watch item**: render-cache pressure — each format×step combination is a distinct
  source, hence a distinct `_svgCache`/worker-cache entry competing for the
  `BrowserRenderCacheMegabytes` budget. Failure mode is benign (miss → re-render).
  Measure on a note-heavy report before tuning.

## Risks

- **`~` ambiguity**: a payload containing a literal `~` immediately before a doubled
  live creole marker reconstructs one character off; if the result still parses, the
  YAML view shows subtly wrong bytes. Rare enough to accept with a code comment; the
  JSON view is always exact.
- **Index alignment**: keyed by original note index + swapped inside
  `buildSourceWithNoteStates` (the single choke point) → minimal new alignment
  surface. Test against filters and fragment splits regardless.
- **Kronikol4J parity**: the Java port (github.com/lemonlion/Kronikol4J) embeds the
  same report JS assets — parity means shipping the same script change there, not
  porting a C# pipeline.

## Rejected alternative: server-side dual generation

Generate the YAML variant in `FormatNoteContent` (where the parsed `JsonDocument`
exists) and embed a per-diagram `{originalNoteIndex: yamlLines}` map beside
`_pumlData`. Byte-exact with zero reconstruction risk, but roughly duplicates every
JSON note body in the report pre-compression, needs a C# YAML emitter + embedding
pipeline mirrored byte-for-byte in Kronikol4J, and does nothing for existing reports.
Reconsider only if reconstruction edge cases turn out to matter in practice.

## Test plan (TDD — red-green-refactor per CLAUDE.md)

**There is no Node/JS unit-test harness in this repo.** JS behavior is tested through
Playwright against locally generated report HTML. Follow the existing conventions:

- E2E project: `tests/Kronikol.Tests.EndToEnd/`. Base classes: `PlaywrightTestBase`,
  `DiagramNotePlaywrightBase` (helpers: `HoverNoteRect(index)`,
  `ClickNoteButton(selector)`, `WaitForSvgReRender(previousHtml)`,
  `DoubleClickFirstNoteAndWait()`, report generators like
  `GenerateLongNoteReport(fileName)`); pages built by `TestPageGenerator.cs` /
  `ReportTestHelper.cs`. Rules (CLAUDE.md): no `{ force: true }`, no network mocking,
  `PollingInterval = 200` on every `WaitForFunctionAsync`, SVG interaction via JS
  `dispatchEvent` (`mouseenter` for hover), `.First`/`.Nth(n)` under strict mode.
- Unit-style coverage of the pure JS functions (reconstructor, token walk, YAML
  emitter): expose them on `window` (e.g. `window._noteFormatInternals`) and drive
  them via `Page.EvaluateAsync` in a dedicated E2E fixture — dozens of input/output
  cases run in one page load, no new test infrastructure.

Coverage, in TDD order:

1. Reconstructor: creole-escaped payloads (each marker class), doubled backslashes,
   wrap-broken long strings (base64), focus markup, gray headers; gate rejects
   truncated (`...` marker), GraphQL, plain-text, binary-placeholder, and
   continuation-chunk bodies.
2. Emitter: nested objects/arrays, block scalar for `\n` strings (including `|`,
   `|-`, `|2-` variants), quoting edge cases, unicode; token-fidelity — int64 beyond
   2^53 verbatim, duplicate keys preserved, integer-like key order preserved;
   quoted-fallback — `\r`/control chars, trailing-space lines, >120-char runs.
3. E2E interaction: button appears on hover for JSON notes only; click re-renders the
   note as YAML with multi-line SQL visible; toggle back restores exact JSON; state
   survives expand/truncate/collapse, header hide, assertion/step/database filter
   toggles, and fragment-split diagrams; SVG copy-text on a YAML note yields the
   block-scalar lines as displayed.

A gold vector for the first red test — note lines (headers + escaped payload):

```
<color:gray>[content-type=application/json]</color>

{
  "id": 9007199254740993,
  "query": "SELECT o.id,\\n       o.total\\nFROM orders o"
}
```

must reconstruct to JSON with `\n` escapes intact and `9007199254740993` verbatim, and
emit:

```yaml
id: 9007199254740993
query: |-
  SELECT o.id,
         o.total
  FROM orders o
```

## Documentation & release (per CLAUDE.md)

- Wiki (`../Kronikol.wiki`): new page/section — eligibility rules, bytes-fidelity
  guarantees and the `~` caveat, interaction with truncation/filters.
- `CHANGELOG.md` entry; patch version bump across **all** packages (same number,
  `Directory.Build.props` / per-package as the repo does today).
- README if its feature list mentions note interactions.
- Kronikol4J: sync the updated script asset(s) + its changelog; if the Java release
  lags, document the divergence in the parity notes.
- Commit, tag `v{version}`, push commit + tag.

## Suggested implementation order

1. Red: `Page.EvaluateAsync` fixture + gold-vector reconstructor test → green:
   reconstructor. Iterate per transform class.
2. Red/green: token walk + YAML emitter, per coverage list.
3. Red/green: conservative re-escape (+ block-scalar wrap exception).
4. Red/green: `_noteFormats` swap in `buildSourceWithNoteStates` + shared re-render
   helper.
5. Red/green: hover button + lazy eligibility in `_noteShowButtons`.
6. E2E interaction matrix (filters, fragments, header-hide, copy-text).
7. Docs, changelog, version bump, tag, push.

Estimate: reconstructor + emitter + fixtures ~1.5–2 days; button + state plumbing
~0.5–1 day; E2E matrix ~1 day; docs/release ~0.5 day. Kronikol4J: copy script assets.
