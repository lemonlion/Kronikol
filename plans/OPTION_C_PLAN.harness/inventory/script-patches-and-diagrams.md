# Option C port inventory: page-script patches, note buttons, diagram look (v5)

Written 2026-10-18. Read-only analysis; nothing in either repository was changed, built or run.

**Baseline**

- Mock: `scratchpad/cmock/`. `build.js` (287 lines) patches a real report's own scripts; `c-round17-buttons.js` (420),
  `c-diagram-look.js` (445) + `.css` (49), `c-round17.js` (123), `c-round5.css`, `c-round7.css`, `c-round17.css`. The
  page was built from a 4.26.4 BreakfastProvider report (`build.js:269`). `c-dom.js` = `c-dom.r5.js` + `c-round6.js` +
  `c-diagram-look.js` + `c-round8/9/9-tips/13/16/17.js` (`build-r9.sh:6`); `c-skin.css` = tokens + August layers +
  `c-extra`, `c-round2..7`, `c-diagram-look`, `c-round8/9`, `c-dark`, `c-contrast`, `c-round16/17` (`compose.py:69`).
- Product: `C:\Code\Kronikol-v5` at `9452ddb5` (4.26.9 plus the v5 dependency-filter default). All product line numbers
  are at that commit.
- Kronikol4J: `C:\Code\Kronikol4J` at `92d6b28` (ledger caught up to .NET 4.16.3).

**Method.** Every `build.js` anchor was re-run with node against the v5 sources, with the `__…_DEFAULT__` tokens
substituted as `DiagramContextMenu.GetCollapsibleNotesScript` does (`DiagramContextMenu.cs:144-158`).
`createNoteButtons` was diffed line by line (CRLF normalised) against the product function and against
`r17-createNoteButtons.js`. Tests were grepped under `C:\Code\Kronikol-v5\tests` with `bin/` and `obj/` left out. "Fails"
means the assertion's text contradicts the change; nothing was executed.

**Abbreviations.** CNS = `src/Kronikol/Reports/collapsible-notes-script.js`; CMS = `src/Kronikol/Reports/context-menu-script.js`;
BRS = `src/Kronikol/Reports/plantuml-browser-render-script.js`; RG = `src/Kronikol/Reports/ReportGenerator.cs`;
PUC = `src/Kronikol/PlantUml/PlantUmlCreator.cs`; CDG = `src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs`;
UT = `tests/Kronikol.Tests`; E2E = `tests/Kronikol.Tests.EndToEnd`; K4J = Kronikol4J.

---

## 0. Summary

| P | What | Product target | Round / item | Port to | v5 anchor | Tests hit (existing) |
|---|---|---|---|---|---|---|
| P1 | Collapsed preview drops a JSON body's braces | CNS `getNotePreview` 97-109 | R5 | JS | matches | 1 theory (2 cases) |
| P2 | "note" renamed payload/box in 5 strings | RG 1461, 1464, 1474, 1477, 1480; CNS 458 | R11 + R16 | C# + JS | matches | 5 asserts, 3 files |
| P3 | Format select gets an optgroup heading | RG 1459-1465 | R17 item 3 | C# | matches | 0 known (reverses a documented decision) |
| P4 | Events/Setup filters injected | CNS before 2208 | R16 | JS | matches | new tests |
| P5 | `_eventsVisible` / `_setupVisible` flags | CNS 2175 + DiagramContextMenu.cs | R16 | JS + C# options | matches | baseline pin |
| P6 | Filters in `composeNoteSource` | CNS 1715-1725 | R16 | JS | **drifted (0)** | none |
| P7 | Filters in `_preProcessSource` | CNS 2406-2415 | R16 | JS | matches | none |
| P8 | Filters in the note-index replay | CNS 776-781 | R16 | JS | **one of two drifted** | none |
| P9 | Exact (marked) note map | CNS 797-798 | R16 | JS | matches | new tests |
| P10 | One-line collapsed payload loses + and ▼ | CNS `createNoteButtons` 324, 464, 498 | R16 item 5 | JS (inside P15) | matches | see P15 |
| P11 | Events/Setup buttons and their gates | RG 1051-1093, 1847-1859, 3373-3383 | R16 | C# | markup | new tests |
| P12 | Local engine swap | BRS 56, 58 | harness | none | matches | none |
| P13 | Open in new tab: tab opened in the click | CMS 692-750 | R17 item 21 | JS | matches (9) | 2 UT facts' premise, 1 E2E |
| P14 | Flame bars from an 8-hue palette | `flame-chart-render-script.js` 5-15 | R17 item 10 | JS + CSS (+ C# public renderer) | matches | 1 UT fact if C# follows |
| P15 | `createNoteButtons` replaced | CNS 318-678 | R17 items 9, 13, 14, 15 | JS | matches | 24 locator lines, 7 E2E files |
| P16 | Page scaffolding, theme boot | RG `<head>` | R5 | partly | n/a | none |
| P17-P31 | Diagram look (source rewrite + role pass) | PUC, CDG, StepBarPlantUml, BRS, CNS | R7, R12, R14, R15, R16, R17 | split, see section 3 | n/a | per item |
| P32-P34 | `c-round17.js`: new-tab helpers, fold groups, scenario settings | CMS; RG 1830-1835; RG scenario toolbars | R17 items 21, 2, 23 | JS + C# | n/a | 213-call helper (P33) |
| P35-P37 | Colours that belong in C# | DependencyPalette, CDG, PUC, NotePalette, Track, InternalFlowRenderer | R5, R7, R17 items 4, 22 | C# + CSS | n/a | 6 asserts for the database colour |

Two anchors no longer match v5 (P6, P8): 4.26.8 (`70b994f0`) rewrote the filter reads as `filterShown(...)`, so
`build.js` would throw `compose: anchor found 0 times` on a v5 report.

---

## 1. `build.js` patches

### P1. Collapsed preview drops a JSON body's braces
- **Mock:** `build.js:50-60` (regex 53, replacement 54), added by `patch-build-r5.js`.
- **Target:** CNS `getNotePreview` 97-109; anchor lines 100-102 match once. Only copy in the product.
- **Change:** after `if (!raw) return '';`, when `raw` starts with `{`: `body` = raw without the leading `{` and spaces,
  `whole` = body without a trailing `}`; return `whole` if it is non-empty, differs from `body` and is at most 60 chars;
  otherwise, if `whole` is non-empty, the cut preview starts after the brace. `{}` stays `{}`. A `[` array is untouched.
- **Why:** "the one-line preview drops a JSON body's opening brace, and its closing one when the whole body fits".
- **Round:** 5.
- **Reach (read from code):** the preview is the collapsed note's drawn line (`buildSourceWithNoteStates`), the
  empty-note test in `makeNotesCollapsible` (875), and so CMS "Copy current box text" of a collapsed note (CMS 542-561
  reads the drawn block) also loses the braces. P10's `kronCollapsedIsWhole` compares against it.
- **Tests:** E2E `NoteHeaderFormsTests.cs:150-173` (Theory, 2 header tags) asserts painted
  `{"item":"Widget","qty":2}` at 166: fails. E2E `KafkaNoteTests.cs:56-72` asserts `{` painted (69); the nested
  `{"Sku"…}` keeps one, expected to pass (needs a run). UT `DiagramContextMenuTests.cs:797-804` passes.
- **K4J:** same anchor in its CNS copy (matches once).

### P2. Wording: "note" becomes payload / box
- **Mock:** `build.js:62-79`, `WORDING` (5 pairs; each must be found).
- **Round:** 11 ("note" is PlantUML's word) and 16 (owner: the boxes are "payloads").
- **Targets and changes:**
  - a. RG 1461 and 1464 (report and scenario format selects): `aria-label` and `title` "Note payload format" become
    "Payload format".
  - b. RG 1474 `noteWidthTitle` "Note width. Wrap: notes wrap at the report's note width. Wide: notes widen to fill the
    diagram." becomes "Payload width. Wrap: they wrap at a set width. Wide: they widen to fill the diagram."; RG 1477 and
    1480 pass label "Note width", which `BuildNoteAppearanceSelect` (1107-1113) writes as both `aria-label` and
    `<optgroup label>`: becomes "Payload width".
  - c. CNS 458 (`createNoteButtons`, width button tooltip): 'Widen this note to fill the diagram' and 'Back to the
    default note width' become 'Widen this box to fill the diagram' and 'Back to the default width'. In the built page
    these are overwritten by P15's swap, which already carries them (`c-round17-buttons.js:192`).
- **Tests:** UT `Reports/ToggleDefaultsMarkupTests.cs:276` (optgroup), 277 (title), 278 (aria-label); E2E
  `NoteAppearanceTests.cs:273` (optgroup label); E2E `NoteFormatDropdownTests.cs:58` (aria-label). 5 asserts in 3 files.
  Nothing tests the widen tooltip. Wiki pages naming the controls need the same words.
- **K4J:** no counterpart (the port has neither select; its ledger already says "Note appearance controls: never ported").

### P3. The format select gets a heading
- **Mock:** `build.js:81-89`, wraps the options of every `<select class="note-format-select">` in
  `<optgroup label="Payload format">`.
- **Target:** RG 1458-1465 (`noteFormatOptions` and both selects). Could reuse `BuildNoteAppearanceSelect`.
- **Round:** 17 item 3 (owner: a heading, as the width dropdown has).
- **Note:** this reverses a recorded decision: RG 1450-1457 "It takes no <optgroup> heading … Chromium indents grouped
  options, which measured 15px of closed-state width per select on a toolbar that has none to spare" (same 15px in the
  `BuildNoteAppearanceSelect` doc, 1101-1104). The comments must change with it.
- **Tests:** UT `ToggleDefaultsMarkupTests.cs:256` asserts the contiguous
  `<option value="json">JSON</option><option value="yaml" selected>YAML</option>`, still contained inside the optgroup.
  No test asserts the absence. Toolbar-width facts may move by about 15px in Chromium (needs a run).

### P4. Events and Setup filters injected into the note script ("filters")
- **Mock:** `build.js:111-122`; bodies from `c-round16-notes.js` (1-96) and `c-round16-strips.js` (1-79).
- **Target:** CNS, inserted before `function stripDatabaseCalls(source)` at 2208, inside the IIFE, where it reaches
  `parseNoteBlocks`, `strip*`, `getNotePreview`, `NOTE_HEADER_TAG`, `syncToggleBtn` (2843), `renderWithPending`
  (2747), `isComponentDiagramContainer` (2204).
- **Change:** adds
  - `stripEventCalls`: a text clone of `stripDatabaseCalls` (CNS 2208-2308) with `(?:database|collections)` replaced
    by `queue` (the build asserts the keyword appears exactly twice: 2211, 2232). Events = calls to and from `queue`
    participants (`DependencyPalette.GetSequenceShape(MessageQueue)` = "queue", `DependencyPalette.cs:85`), their
    trailing positional notes, explicit notes and activations. `<<eventNote>>` is not used (it also marks BigQuery,
    Bigtable and Spanner requests).
  - `stripSetup` (`strips.js:6-79`): drops `partition … Setup … end` (SeparateSetup; PUC 210 writes
    `partition {colour} Setup` or `partition Setup`), else everything from the first content statement to the first
    When step bar (WHEN rule `strips.js:10` keys on
    `hnote across <<stepDelimiter>>( #black:<color:white>` or `<<stepBody>>: )` then optional `<b>` then `when`); returns
    the source unchanged when a cut would leave a block open; then drops participants nothing left calls.
  - `kronFlag`, `kronSetupFilter`, `kronEventsFilter`, `kronExactNoteMap`, `kronCollapsedIsWhole`, `buildKronQueue`,
    `kronToggle`, and `window._toggleEvents`, `_toggleSetup`, `_toggleScenarioEvents`, `_toggleScenarioSetup`.
- **Round:** 16.
- **Port notes:** make `stripDatabaseCalls` keyword-parameterised instead of cloning its text; read flags through
  `filterShown` (CNS 1711-1713, F2); one toggle model for all five filters (F5).
- **Tests:** new ones needed (unit: `stripSetup` and `stripEventCalls` on emitter shapes including SeparateSetup and the
  rich step bar; Playwright: report and scenario toggles, a later-drawn diagram, duplicate "1 row" notes keeping their
  buttons). Existing script-text facts keep passing: UT `DatabaseToggleReportTests.cs:89-90, 152`,
  `NoteFormatToggleScriptTests.cs:31-39, 49, 92`, `ScenarioTruncateAndCollapsibleNotesTests.cs:93-94`.

### P5. Flags ("flags")
- **Mock:** `build.js:123`.
- **Target:** CNS 2175 `window._databasesVisible = __DATABASES_VISIBLE_DEFAULT__;` (globals block 2166-2177).
- **Change:** appends `window._eventsVisible = true; window._setupVisible = true;` (hard-coded).
- **Round:** 16.
- **Port:** tokens `__EVENTS_VISIBLE_DEFAULT__` and `__SETUP_VISIBLE_DEFAULT__` in `GetCollapsibleNotesScript`
  (`DiagramContextMenu.cs:144-158`); `bool? EventsShown`, `bool? SetupShown` on `ReportToggleDefaults`
  (pattern at `ReportToggleDefaults.cs:24-33`) and the resolver (`ReportToggleDefaultsResolver.cs:19-22, 75-76`). New
  public options.
- **Tests:** UT `ToggleDefaultsBaselineTests` (unset config is byte-identical to BuiltIn) must still hold with the new
  options; UT `ToggleDefaultsMarkupTests.cs:99, 123` are the pattern for the new flags.

### P6. Filters in `composeNoteSource` ("compose") - anchor drifted
- **Mock:** `build.js:124-125`.
- **Target:** CNS `composeNoteSource` 1715-1725, the one rebuild chain every re-render path uses.
- **Change:** `kronEventsFilter(applyDatabasesFilter(applyStepsFilter(applyAssertionFilter(kronSetupFilter(built,
  container), …), …), …), container)`: setup first (it finds the When bar the steps strip removes), events last.
- **v5:** the mock's anchor reads `!!container._assertionsVisible` etc.; v5 has `filterShown(container, '_…Visible')`
  (1722-1724), so it matches 0 times. Port the composition onto the `filterShown` form.
- **Round:** 16.
- **Tests:** UT `NoteFormatToggleScriptTests.cs:31-39` (formats before build) unaffected.

### P7. Filters in `_preProcessSource` ("preprocess", "preprocess-events")
- **Mock:** `build.js:126-129`.
- **Target:** CNS `window._preProcessSource` 2406-2415. Both anchors match (2412-2413, 2415).
- **Change:** after `el._databasesVisible = window._databasesVisible;` sets `el._eventsVisible = kronFlag(el,
  '_eventsVisible')` and `el._setupVisible = kronFlag(el, '_setupVisible')`; starts `renderSource` from
  `kronSetupFilter(source, el)`; after the databases strip applies `kronEventsFilter`.
- **Semantics:** `kronFlag` takes the enclosing `details.scenario`'s own choice first, then `window[flag] !== false`. The
  existing three flags take only the report's value here (2410-2412). See F5.
- **Round:** 16. **Tests:** none existing.

### P8. Filters in the note-index replay ("replay", "replay-events") - one anchor drifted
- **Mock:** `build.js:130-131`.
- **Target:** CNS `makeNotesCollapsible`, the `filteredToOrigMap` block 776-799.
- **Change:** `var cleanFilt = owner._noteOriginalSource;` (778, matches) becomes
  `kronSetupFilter(owner._noteOriginalSource, owner)`; after the databases strip, `kronEventsFilter`.
- **v5:** the second anchor `if (!owner._databasesVisible) cleanFilt = …` is now
  `if (!filterShown(owner, '_databasesVisible')) …` (781): 0 matches.
- **Round:** 16. **Tests:** none existing.

### P9. Exact note map ("exact-map")
- **Mock:** `build.js:132-133`; `kronExactNoteMap` at `c-round16-notes.js:20-41`.
- **Target:** CNS 797-798, after the greedy map (783-797).
- **Change:** when setup or events are hidden, each note is marked `kron-note-N` after its opener, the marked source goes
  through the same filters, and the marks read back give the map; used when shorter than the original note list.
- **Why** (`notes.js:17-19`): two notes that read alike ("1 row" in the setup and again later) are confused by the
  greedy text match. `kronExactNoteMap` reads raw `owner._assertionsVisible/_stepsVisible/_databasesVisible` (30-32),
  not `filterShown` (F2). The greedy match has the same flaw for the existing filters (F6): port the marked map for
  every filter.
- **Round:** 16. **Tests:** new.

### P10. One-line collapsed payload loses + and ▼ ("whole", "plus", "expand")
- **Mock:** `build.js:134-137`; `kronCollapsedIsWhole` at `c-round16-notes.js:45-55`.
- **Target:** CNS `createNoteButtons`: 324 (`var longNote …` gains `var kronWhole = state === 'collapsed' &&
  kronCollapsedIsWhole(contentLines, hdrHidden);`), 464-465 (`if (state === 'collapsed' && !kronWhole)`), 498-499
  (`if ((state === 'collapsed' && !kronWhole) || state === 'truncated')`).
- **Rule:** a collapsed note with at most one shown line (headers hidden, or none) whose preview equals that line.
  Double-click still cycles it.
- **Round:** 16 item 5. In the built page P15 overwrites the whole function and carries these three edits
  (`c-round17-buttons.js:7, 200, 235`).
- **Tests:** see P15.

### P11. Events and Setup buttons, and their gates
- **Mock:** `build.js:140-180`. Node gunzips `#puml-data`, runs `stripEventCalls`/`stripSetup` over every teoz source,
  maps diagrams to scenarios by document position, then appends
  `<button class="details-radio-btn toggle-btn details-active" data-toggle="events" data-shown="true"
  onclick="window._toggleScenarioEvents(this)">Events Shown</button>` (and Setup) after each scenario's Databases button
  when one of that scenario's diagrams has them, and after the report bar's Databases button when any diagram has.
- **Targets:** RG `BuildFilterToggleButton` 1051-1056 (produces exactly this markup for "events"/"setup"),
  `BuildScenarioDiagramToolbar` 1076-1093, the report toolbar 1847-1859, the no-sequence-tab branch 3373-3383; gate
  pattern `hasDatabaseParticipants` 1441-1443.
- **Open points:** the mock gates scenario buttons per scenario, the product's gates are report-wide (RG 3373-3375); the
  mock inserts only beside an existing Databases button (F11); a C# Setup gate needs the `stripSetup` rule in C#.
- **Round:** 16. **Tests:** new markup facts; toolbar-width facts see two more buttons where fixtures have queues.

### P12. Local engine swap (harness)
- **Mock:** `build.js:183-201` (`ENGINE=…`): BRS `var ENGINE_URL = '…/@plantuml/core@…/plantuml.js';` (56) and
  `var ENGINE_INTEGRITY = 'sha256-…';` (58) swapped for a Blob URL of a local (Wasm GC) build and its own SRI.
- Not a port item.

### P13. Open in new tab: the tab is opened in the click
- **Mock:** `build.js:203-226` (swaps) + `c-round17.js:1-29` (helpers).
- **Target:** CMS, the `contextmenu` handler (395-): "Open image in new tab" 692-711 (PNG 693-699, PNG without
  transparency 700-706, SVG 707-710) and six text items: `origSource2` 717, `source` 721 and 727, `_fullNoteText` 736
  and 746, `_currentNoteText` 740. All 9 match at v5 (the build needs at least 6).
- **Change:** PNG: `(function (w) { svgToCanvas(svg, function(canvas) { window.__kOpenPng(w, canvas); }); })(window.__kOpenTab());`
  (same for `svgToCanvasWithBg`); SVG: `window.__kOpenSvg(serializeSvg(svg))`; text: `window.__kOpenText(<var>)`.
  `__kOpenTab` = `window.open('', '_blank')`; `__kOpenPng` writes `<img src=canvas.toDataURL('image/png')>` into it
  (document.open/write/close, ground #f5f7fb); `__kOpenSvg` writes `<img src="data:image/svg+xml;base64,…">`;
  `__kOpenText` writes an escaped `<pre>` (pre-wrap, 13px mono). Each falls back to the old `blob:` open when no tab.
- **Why** (`c-round17.js:1-5`): Firefox refused the PNG items (opened after the render, outside the click) and a
  `blob:` URL made by a `file://` page has an opaque origin.
- **Round:** 17 item 21.
- **Port:** helpers inside CMS's IIFE, not `window.__k*`. `createMenuItem` (197-206) runs the action inside the click, so
  the synchronous open has user activation. Every text tab is titled "Diagram source", box text included (F12).
- **Tests:** UT `DiagramContextMenuTests.cs:1219-1226` asserts no `readAsDataURL` and no
  `window.open('data:image/svg+xml` (still true, but its premise "blob URLs not data URIs" is reversed) and 1229-1233
  asserts `window.open(URL.createObjectURL(blob))` is present (passes only if the fallback keeps that text). E2E
  `NoteCopyFidelityTests.cs:81-100` reads the popup's `body.innerText`; should pass on the written page (needs a run).
  Playwright's Firefox does not block popups, so a test can only check that `window.open` runs before the canvas
  callback.
- **K4J:** the same 9 sites in its CMS copy (525-576).

### P14. Flame bars from a palette
- **Mock:** `build.js:228-245` (from `r10-patch-build.py`) + `c-round17.css:98-110`.
- **Target:** `src/Kronikol/Reports/flame-chart-render-script.js` `buildBar` 5-15 (anchor at 11 matches).
- **Change:** inline `background:hsl(' + hue + ', 60%, ' + lightness + '%)'` (hue = |hashCode(source)| mod 360,
  lightness 70 + min(depth x 5, 20)) becomes `--fh:' + FLAME_HUES[|hashCode(source)| mod 8]` with
  `FLAME_HUES = [212, 262, 188, 338, 152, 32, 290, 8]` declared before `buildBar`. CSS: height 20px, margin 2px 0,
  `border-left: 3px solid hsl(var(--fh,215) 42% 54%)`, radius 4px, `background: hsl(var(--fh,215) 58% 93%)`, ink text,
  hover 62% 87%, dark 26% 21% with edge 48% 60%, label padding 0 6px, `.iflow-flame` padding 6px 0, boundary marker
  `color-mix` 16%/45%. Replaces `internal-flow-popup-styles.css:79-92`.
- **Round:** 17 item 10. The swap leaves `hue`, `lightness` and the `depth` parameter dead.
- **C#:** the report draws every flame chart in JS from `data-flame-z` (`InternalFlowHtmlGenerator.cs:437, 477, 501`).
  `InternalFlowRenderer.RenderFlameNode` (`InternalFlow/InternalFlowRenderer.cs:354-379`, hue 366-369) is public API whose
  comment ties its hue to the browser's; port it too or the two disagree.
- **Tests:** UT `Reports/CultureInvariantOutputTests.cs:234-253` asserts `background:hsl({hue}, 60%, ` from the C#
  renderer (fails only if C# follows). UT `InternalFlowRendererTests.cs:397, 429` and the E2E flame tests (locators,
  titles) are unaffected.
- **K4J:** same `buildBar` in its copy (5-11); no Java server-side flame.

### P15. `createNoteButtons` replaced
- **Mock:** `build.js:247-265` swaps the whole function for `c-round17-buttons.js`.
- **Target:** CNS `createNoteButtons` 318-678 (called once, 954).
- **Provenance:** `r17-createNoteButtons.js` is the round-16 page's function (product + P2c + P10); `r17-make-buttons.py`
  applies items 13-15, `r17-keepplace.py` item 9. The v5 function equals the 4.26.4 one the mock extracted (the diff shows
  only the P2c and P10 lines).
- **Round:** 17 items 9, 13, 14, 15 (and carries R16 item 5 and the R11/R16 wording).
- **Behaviour:** section 2.
- **Artifact:** the file ends with a copy of the "Fragment note indexing" comment (`c-round17-buttons.js:411-420`), so the
  built page has that comment twice (F13).

### P16. Page scaffolding
- **Mock:** `build.js:15-48` (strips the report's structural tags; the scanner skips `script`/`style` because the report
  carries whole HTML documents in strings) and `267-287` (title, theme boot, C stylesheet and scripts, preview note,
  local skeleton).
- Only the theme boot is product-relevant (R5): `localStorage 'kronikol.theme'`, else `prefers-color-scheme`, written as
  `data-theme` on `<html>` before first paint. It would go into RG's `<head>` (1518-1573) ahead of the stylesheet.

---

## 2. `createNoteButtons`: `c-round17-buttons.js` against CNS 318-678

- **B1 (R16 item 5).** A collapsed note that already shows all of itself gets no + and no bottom ▼ (P10).
- **B2 (item 9, `keepPlace` 9-36).** Wraps − (142), the top ▲ (116), the bottom ▲ of an expanded long note (296), the
  new bottom collapse (272), and double-click on the note's paths (352) and hover rect (404). It records the click's
  `clientY` and the note's hover-rect index (first `svg .note-hover-rect` in `container` containing bbox.x+1, bbox.y+1),
  runs the action, and if the click was in the lower half of the note polls every 50 ms, up to 10 s, for a new `svg` in
  `container` with `!container._noteRendering` and a hover rect; if that note got shorter it scrolls by
  `rb.bottom - min(y + 8, innerHeight - 24)`. Not wrapped: + (228), bottom ▼ (255), format (180), width (93). For a
  fragment it never scrolls (F4).
- **B3 (item 14, 39-62, 301, 337).** Every top-right button is created at the first slot,
  `bbox.x + bbox.width - topSize - pad` (glyph button 68, ▲ 102, format 154), and moved left by
  `transform="translate(-k*(topSize+pad), dy)"`; hidden (`display:none`) buttons are skipped, so a hidden format or width
  button leaves no gap. Order from the right: −, ▲ (expanded long), format, width; + alone when collapsed. Product: fixed
  slots (glyph button 333 `topSize*slot + pad*slot`, ▲ 367 slot 2, format 421 slot 2 or 3, width slot 3 or 4): a hidden
  format button leaves a 17px gap. The `slot` parameter (67) and the format `slot` variable (153) become unused.
- **B4 (tall notes, height > 500, 323-337).** The product rewrites the `y` of rect, lines and text of the buttons named
  minus/plus/format/width (576-605), so the ▲ (no `data-note-btn`) stayed at the top. The mock passes `dy` through the
  packing transform: the ▲ follows the visible part too; the unused `noteScreenBot` goes.
- **B5 (item 13, `kTriangle` 45-53).** ▲/▼ were `<text>` glyphs (top ▲ font-size 12, bottom 10, fill #666; 379-385,
  514-520, 542-548). Now `<polygon>`: height h-4 (2px clear of top and bottom), width 1.25 x height, fill #666,
  pointer-events none (10 x 12.5 in the 14px button, 8 x 10 in the 12px bottom buttons). `c-round17.css:43` inks them
  `var(--dg-icon-ink)`.
- **B6.** New `data-note-btn` names: top ▲ "truncate" (114), bottom ▼ "expand-bottom" (253), expanded-long bottom ▲
  "truncate-bottom" (294), new "collapse-bottom" (262). The product had none on these three.
- **B7 (item 15, 234-276).** A truncated note's bottom holds ▲ (`collapse-bottom`, `onContract`, i.e. collapse like −)
  and ▼ (`expand-bottom`, `onExpand`), each 24px wide with 3px between, centred as a pair. A collapsed note keeps one 36px
  ▼. Product: one 36px ▼ for both.
- **B8.** Width tooltips: 'Back to the default width' / 'Widen this box to fill the diagram' (192).
- **B9. Unchanged:** sizes (12, 14, pad 3), button rects (#ffffff, #999, 0.5, rx 2), the J/Y format glyph and its
  tooltips, the 300 ms hide, lazy eligibility, the collapsed-note tooltip, the hover rect.
- **Tests:** 24 code lines in 7 E2E files find buttons by glyph text (`text` containing ▲ or ▼) and stop finding them:
  `DiagramNotePlaywrightBase.cs` 146, 153 (helper `ClickDownArrowAndWait`, 2 callers), `DiagramNoteLongNoteTests.cs`
  52, 101, 128, 147, 163, 170, 178, 234, 252, 270, `NoteButtonsAfterHeaderHideTests.cs` 238, 258, 267, 280,
  `NoteExpandArrowHeaderHiddenTests.cs` 61, 76, 94, `NoteYamlToggleTests.cs` 375, 385, `YamlNoteCopyTextTests.cs` 33,
  38, `ContinuationNoteInteractivityTests.cs` 321. The B6 names are the stable replacement. `DiagramNoteSplitTests.cs:196-200`
  (icons at least as many as hover rects) is at risk only in a collapsed-start fixture (B1); the other icon counts assert
  > 0 or a floor. Rect-x readers (`ContinuationNoteInteractivityTests.cs:315, 395, 545`, `NoteAppearanceTests.cs:87`) only
  check x lies within the note: still true. No test covers B2, B3, B4 or B7.

---

## 3. `c-diagram-look.js` and `c-diagram-look.css`

Levers on by default: font, geometry, services, cards, bands, strips (28); badges opt-in through
`window.__kronikolLookLevers`. Two halves: a source rewrite at render time inside a wrapper round `window.plantuml.render`
and `prefetch` (95-116), and a role pass by structure after drawing (156-427), driven by a document MutationObserver plus an
IntersectionObserver for SVGs not yet laid out (429-443).

Facts that decide where each step goes:
- Kronikol's sequence diagrams are teoz, and the teoz renderer emits no semantic classes or groups (UT
  `PlantUml/NodeJsPlantUmlRendererTests.cs:675`; memory "PlantUML DOM-native output"). Any per-element role must be found
  from geometry after drawing.
- `PUC.CreatePlantUmlPrefix` (1194-1219) is the only emitter of `!pragma teoz true`: one place for a source change.
- The page also draws sources written by older reports (`kronikol merge`) and ingested runs. A C# change does not reach
  them; a render-time rewrite does.
- Colours must follow the page's light/dark switch without re-rendering (the shim and CNS `_svgCache`, 1901, key on the
  source), so theme colours stay in CSS. Light-theme values can be written by C#.
- `serializeSvg` (CMS 74-79) and `svgToCanvas` (121-136) carry no page CSS into exports (F9).

### P17. Font (source)
- **Mock:** 35 `skinparam defaultFontName system-ui` after `!pragma teoz` (`insertAfterHeader` 50-58); a diagram with
  `left to right direction` / `top to bottom direction` gets it after `@startuml` (87-88). Round 7.
- **Why** (13-14): the engine measures and paints the same face; a font list is quoted into one unknown family (Times New
  Roman).
- **Port to C#:** PUC `CreatePlantUmlPrefix` after 1210; CDG non-C4 preamble after `left to right direction` (103). Gate
  to BrowserJs (and NodeJs inline SVG): Server/Local use the JVM and the C4 branch (`ComponentDiagramReportGenerator.UsesC4`,
  73) and have no `system-ui` family. Decide whether a user `!theme` (written before the pragma, 1205, 1210) keeps its font.
- **Tests:** none assert the absence; UT `PlantUmlCreatorTests.cs:2798-2811` (theme before pragma) and 2284-2287 (pragma
  before partition) unaffected.

### P18. Geometry (source)
- **Mock:** 36 `skinparam shadowing false`, `skinparam SequenceMessagePadding 4`, `skinparam sequenceMessageAlignment
  center`; `<style> sequenceDiagram {…}` 38-43: participant {RoundCorner 12; Padding 8 14; Margin 0 10; LineThickness 1};
  actor, database, queue, collections, boundary, control, entity {Margin 0 10; LineThickness 1}; lifeLine {LineStyle 0;
  LineThickness 1}. Round 7.
- **Port to C#** with P17 (pure source, no geometry; about 15 lines per sequence source). Same theme question.
- **Tests:** none pin the absence; `skinparam wrapWidth` facts (UT `DiagramNoteWrapWidthTests.cs:30, 36, 63`,
  `PlantUmlCreatorTests.cs:444`) unaffected.

### P19. Cards (source part)
- **Mock:** 42 `note { LineThickness 1 }` in the same style. Round 7. **Port to C#** with P18.

### P20. Services drawn as boxes (source)
- **Mock:** 74 `^entity (".*?"|\S+)` becomes `participant $1`. Round 7.
- **Port to C#:** `DependencyPalette.GetSequenceShape(HttpApi)` "entity" becomes "participant" (`DependencyPalette.cs:82`,
  public) and PUC 1402 (`actorDefined ? "entity" : "actor"`). Readers of declarations already accept `participant`: RG
  `ExtractDependencies` 6292-6308 and `ParticipantRegex` 6310, PUC regex 1919, BRS `_statementDeclarationRx` 1046,
  `stripSetup`'s DECL.
- **Tests:** 12 asserts on `entity "…` in UT `PlantUml/PlantUmlCreatorTests.cs`. `ExtractDependenciesTests`,
  `SearchDataAttributeTests` use `entity` in inputs only.
- **K4J:** `DependencyPalette.java:20` `HTTP_API("entity", …)`.

### P21. Step bars as rectangles (source)
- **Mock:** 78 `^hnote across (<<stepDelimiter>> #black:<color:white>)` becomes `rnote across $1<b>`; 82
  `^hnote across (<<stepDelimiter>><<stepBody>>: )` becomes `rnote across $1<b>`. Round 7; rich form round 14.
- **Recommendation:** keep it a render-time rewrite in the page (one function in the shim, applied before its cache key).
  Emitting `rnote` from C# means two shapes everywhere: CNS `stripStepDelimiters` (2187 `hnote across <<stepDelimiter>>`),
  `stripSetup`'s WHEN rule, BRS `findOverLongStatement`/`isNoteStatement`, `PlantUmlStatementLimits` (caps measured on the
  hnote prefixes), `InteractionRecord`, `CucumberFeatureSynthesizer`, `StepBarPlantUml.LegacyPrefix`/`RichPrefix` (55-56),
  while older sources stay `hnote`.
- **If moved to C# anyway:** 20 asserts across 21 test files pin the hnote prefixes (UT `StepBarPlantUmlTests`,
  `PlantUmlCreatorTests`, `InteractionRecordTests`, `PlantUmlStatementLengthTests`, `Tool/*`, `Reports/*`; E2E
  `LongStatementRenderingTests`), plus K4J `StepCollector.java:84`. `<b>` adds 3 characters to a bar capped at
  `MaxColouredNoteBarChars` 600 (measured edge 880 with optimizing compilers off): within margin.

### P22. No blank line under a step-bar table (source)
- **Mock:** 76 strips trailing `\n` escapes from a `<<stepBody>>` bar. Round 17 item 6 (owner: no gap under it).
- **Port to C#:** `StepBarPlantUml.Build` adds the trailing pad on purpose (118-119 `if (padded) body.Add("")`; doc 24-27).
  Older sources keep the pad unless the page also strips it.
- **Tests:** UT `PlantUml/StepBarPlantUmlTests.cs` 101, 111, 144, 153, 273, 301 (6 asserts end in `\n`); E2E
  `StepTableBarTests.cs:62-92` asserts the spacer line under the last row (90). Both rewritten by design.

### P23. Render hook
- **Mock:** 95-116 wraps `render` (marks the target `data-k-sequence="1"`, stores `_kNoteKinds`) and `prefetch`; installs a
  setter on `window.plantuml` if the shim is not there yet. Round 7.
- **Product:** BRS creates the shim (`window.plantuml = shim`, 490); render callers are BRS `processQueue` (1301), the CNS
  re-render paths and `internal-flow-popup-script.js`.
- **Port:** fold any page-side source rewrite (P21, and P22 for old sources) into the shim itself; mark sequence containers
  in C# (RG knows the kind) or by `!pragma teoz`; read note kinds from the drawn `data-plantuml` (as 318-327 does).

### P24. Lifelines and participants (role pass)
- **Mock:** 176-185, 210-214, 337-340: lifelines are vertical lines whose x covers more than a third of the height;
  participants are shapes above the lifelines' top or below their bottom that straddle a lifeline x. Classes
  `k-lifeline`, `k-participant` (+ `k-participant-fill`). CSS 33-35: `--dg-lifeline`, `--dg-participant-edge`,
  `--dg-participant`. Round 7.
- **Keep as page post-render** (geometry; no classes from teoz). A colour-only alternative: C# skinparams for the light
  theme, with CSS keyed on those literals for dark (c-round5's method), at the cost of literal aliasing (P36).

### P25. Step bands, their text and table rules (role pass)
- **Mock:** 216-234, 341-345. Spans = filled rect/path/polygon from the leftmost to the rightmost lifeline; step bands =
  spans that are not polygons and are black: `k-step`; texts whose baseline lies in a band: `k-step-text`; unfilled
  line/path/polyline/rect in a band: `k-step-rule`. Round 7; round 12 restricted it to shapes the source identifies (an
  assertion hexagon #d4edda/#f8d7da, the Test bar `TrackingDiagramOverride.cs:88`, a Row marker `TabularInputs.cs:85`, a
  render error `DefaultDiagramsFetcher.cs:79` keep their own look). Depends on P21 (rnote draws a rectangle).
- CSS 41-44: `--dg-step`, `--dg-step-edge`, `--dg-step-ink`, `--dg-step-rule` (overridden by `c-round17.css:50` with
  `--kron-hairline`).
- **Keep as page post-render.** A light-theme-only C# alternative (legacy `#black:<color:white>` and the `.stepBody` style
  PUC 1252-1262 `BackgroundColor black; FontColor white; LineColor white` set to band colours) breaks E2E
  `StepTableBarTests.cs:52-58` (white cell text) and the `#000000` polygon mapping.

### P26. Step-bar tables on their own ground (role pass)
- **Mock:** 236-252, 358-394. For each band holding rules: `rect.k-step-table` over the rules' box (CSS 46
  `--dg-step-table` #ffffff / #131a24), inserted after the band; the header row, down to the second horizontal rule:
  `rect.k-step-table-head` (`--kron-strip`) and its texts `k-step-table-head-text` (`--kron-ink-soft`); then the band's
  texts, rules and both rects get `translate(0,-1.5)` (`k-step-lift`). Round 16; header and lift round 17 item 6.
- **Keep as page post-render** (rule boxes are measured; the lift is paint-only). Creole cell colours in
  `StepBarPlantUml.TableRow` (133-143) are a possible C# route, unmeasured: they lengthen the statement and change caps.

### P27. Strips full width (role pass)
- **Mock:** 253-299, 346-357. Every step band and every across-hexagon (assertion, Test bar, Row marker) is stretched to the
  drawing's content edges, stopping 6px short of anything else in its rows (a frame such as `partition Setup` or a loop is a
  wall); its contents move by dx to the new centre (`moveX` 130-151 rewrites rect x/width, line x1/x2, text x, polygon
  points, absolute path coordinates, relative paths by transform). Round 15: the engine sizes `across` notes to max(participant
  span, own text), centred; 46 of 54 two-participant diagrams were ragged.
- **Keep as page post-render** (geometry; PlantUML has no full-width option).

### P28. Notes drawn as cards (role pass)
- **Mock:** 300-317, 318-327, 395-405. A filled path followed by a small filled path at one of its corners (the fold) is a
  note; its `d` becomes a rounded rectangle of the same bounds (r = min(5, w/4, h/4)); class `k-card` and
  `k-card-<kind>` from the drawn source's note stereotypes (`eventNote` gives `k-card-event`); the fold gets `k-fold`
  (CSS 39 `visibility:hidden`, kept so CNS `findNoteGroups`/`hasNoteFoldTriangle`, 124-263, still find the note). CSS
  37-38: `--dg-note`, `--dg-card-edge`; events `--dg-event` / `--dg-event-card-edge` (round 17 item 4 values). Round 7; kinds
  read from the drawn source in round 17.
- **Keep as page post-render.** A C# `rnote` would remove the fold the note script, context menu and filters depend on
  (`parseNoteBlocks` 83 `note left|right`, `stripDatabaseCalls` positional notes 2245, CMS note matching). Bug F3 in the
  kind regex (63).

### P29. Number badges (off)
- **Mock:** 195-206, 328-334, 406-423; off since round 8. `rect.k-badge` and `text.k-number` by class (the internal-flow
  link binder copies a message line's first text fill). **Drop, or keep as an opt-in lever.**

### P30. Observers and telemetry
- **Mock:** 156-165, 424-444: once per svg, `data-k-roles` holds JSON counts and ms timings; returns false while the svg has
  no width and waits on an IntersectionObserver; `window.__kronikolDiagramLook` exposes transform, levers, tagRoles.
- **Port:** call the role pass explicitly where the post-render hooks already run, before `makeNotesCollapsible`: BRS
  `onQueueItemDone` 1228-1244 (`describeEngineFailure`, `bindIflowLinks`, `_makeNotesCollapsible`,
  `_addAssertionTooltips`, `_addZoomButton`) and the CNS re-render paths (1977-1981, 2053-2057, 2076-2087, 2529-2530,
  2602-2603, 2621-2632). Keep a visibility wait for fragments created `display:none` (1954). Decide whether CNS
  `_svgCache` stores tagged markup (tagged markup carries `data-k-roles`, so a re-tag is skipped).
- The look reaches `.plantuml-browser` only (BrowserJs and internal-flow popups, `InternalFlowHtmlGenerator.cs:394-401`);
  NodeJs `.plantuml-inline-svg` and Server/Local images get none of it (F10).

### P31. `c-diagram-look.css`
- Role tokens with dark values (6-31) and doubled-class `!important` rules (33-49) that beat c-round5's attribute rules.
- **Port:** a product stylesheet beside `inline-svg-styles.css` (`DiagramContextMenu.GetInlineSvgStyles`), shipped with the
  BrowserJs scripts; dark values under `:root[data-theme="dark"]` (the product has no theme today).

---

## 4. `c-round17.js`: parts that touch product scripts

### P32. Item 21 helpers
See P13 (`c-round17.js:1-29`).

### P33. Item 2: expand and collapse per level
- **Mock:** `c-round17.js:31-96`; CSS `c-round17.css:82-91`.
- **Target:** RG 1830-1835 emits `<button class="collapse-expand-all" onclick="toggle_expand_collapse(this,
  'details.feature'|'details.scenario', …)">`, seeded "Collapse All …" when `FeaturesExpanded`/`ScenariosExpanded`
  (1832-1834); `report-collapse-expand-all-function.js:1-6` flips by label text; CSS `stylesheets.css:520-529, 1780`,
  `Constants/Stylesheets.cs:68`.
- **Change:** at load each button becomes `span.k-fold-group[role=group]`: an uppercase label (FEATURES/SCENARIOS) and two
  28x28 icon buttons (expand, collapse), `aria-disabled` when everything is already open or closed (recomputed on every
  `toggle` event, capture phase, coalesced by rAF). Expanding scenarios opens their features too. Round 17 item 2.
- **Port:** emit the group in RG; replace `toggle_expand_collapse`; compute `aria-disabled` at load from the seeded state.
- **Tests:** E2E `PlaywrightTestBase.cs:197-201` `ExpandFirstScenarioWithDiagram` clicks both buttons by label and is called
  213 times; 106 lines in 27 test files name the class, the labels or the function (e.g. UT
  `ToggleDefaultsMarkupTests.cs:409, 418`, `CollapseExpandAllReportTests.cs:41, 57, 58`, `StylesheetRulesTests`).
- **K4J:** identical `report-collapse-expand-all-function.js`; buttons at `DotNetHtmlReportRenderer.java:819`.

### P34. Item 23: scenario settings hidden by default
- **Mock:** `c-round17.js:98-123`; CSS `c-round17.css:123-131`. A "Scenario settings" switch (`button.k-flag`,
  `aria-pressed`) before the mock's `.sort-group` in `.features-tools-line`; `body.k-scenario-settings` shows
  `details.example-diagrams > .diagram-options-bar`; remembered in `localStorage 'kronikol.scenarioSettings'`.
- Touches no product script: `.features-tools-line`, `.sort-group` and `.diagram-options-bar` are mock DOM (`c-dom.js`
  `splitToolbar` 69-87 splits the product's `<div class="diagram-toggle">` into tabs and options).
- **Product target:** RG scenario toolbars (`<div class="diagram-toggle">` at 2311, 2327, 2340, 3361, 3392; controls from
  `BuildScenarioDiagramToolbar` 1076-1093).
- **Tests:** 28 lines in 10 E2E files drive scenario-level controls (`.diagram-toggle … details-radio-btn | toggle-btn |
  note-format-select …`, `_toggleScenario*`, `_setScenario*`, `_setAllNotes`); hidden by default they need the switch on.

---

## 5. Colours that belong in C#

### P35. Databases in deep cyan (decided)
- **Mock:** `c-round17.css:112-121` (round 17 item 22): red read as a failure; the cyan is at least 27 (CIE76) from every
  other dependency colour and from pass, fail and warning. White on #0E7C86 measures 4.95:1.
- **C# changes:**
  - `DependencyPalette.cs:49` `[DependencyType.Database] = "#E74C3C"` becomes `#0E7C86`. `DefaultColors` is public, so this
    is a default change (v5). It reaches sequence arrows (`GetArrowColor` PUC 2081-2095; `SequenceDiagramArrowColors`
    defaults true, `ReportConfigurationOptions.cs:510`), participant fills when `SequenceDiagramParticipantColors` (off by
    default, 513; PUC 1340, 1390, 1417), and component edges (CDG 255-258; `ArrowColorMode.DependencyType` is the default,
    `ComponentDiagramOptions.cs:61`). `DependencyColors` overrides (`ReportConfigurationOptions.cs:540`) still win.
  - CDG 130-134 `skinparam database { BackgroundColor #E74C3C; FontColor #FFFFFF; BorderColor #C0392B }` (non-C4, i.e.
    BrowserJs and NodeJs). The mock paints the box `--dg-db-fill` #0B6C75 in both themes (white name 6.15:1;
    `c-round17.css:116-117`, replacing c-round7's #c0392b) and the border `--dg-db-edge` #09616A light / #2F8F99 dark
    (119-121). So BackgroundColor #0B6C75 (or #0E7C86 if one value is wanted) and BorderColor #09616A.
  - `GetComponentShape` maps Storage to `database` as well (CDG 414-422): Storage boxes change with it; Storage edges stay
    #2ECC71. `ComponentDiagramDiffer.cs` has no database skinparam.
- **CSS keyed on the old literals moves with them:** `c-round5.css:106-107`, `c-round7.css:100`, `c-round17.css:121`.
- **Tests:** UT `ComponentDiagram/ComponentDiagramGeneratorTests.cs:1034, 1097` and `PlantUml/PlantUmlCreatorTests.cs:3151,
  3152, 3221, 3306` assert #E74C3C (6 asserts, 2 files; the `DoesNotContain` at 1080 and 3266 still pass). E2E
  `ComponentDiagramDatabaseToggleTests.cs:113` reads a hand-written fixture (`ReportTestHelper.cs:525`): passes.
  Hand-written fixtures carrying #E74C3C keep working (E2E `ReportTestHelper.cs` 26 lines, `LargeNoteSplitTests.cs` 7,
  `LongStatementRenderingTests.cs` 4, `ColoredArrowSplitTests.cs` 1; UT `DatabaseToggleReportTests.cs` 3,
  `NodeJsPlantUmlRendererTests.cs` 2).
- **Docs:** wiki `Component-Diagrams.md`, `Diagram-Customisation.md`, `Event-Annotations.md`,
  `Integration-TcpTap-Extension.md` name #E74C3C.

### P36. Every other literal the mock remaps
Selectors are scoped to `:is(.plantuml-browser, .iflow-diagram) svg`. Light / dark values from `c-round5.css:64-89` and
`c-dark.css:60-83` unless noted. "Tests" counts assert lines that would change if the C# literal changed.

| # | Literal and selector | What writes it | New token, light / dark | Tests if C# changes it |
|---|---|---|---|---|
| P36.1 | `text[fill="#000000"]` (c-round5 91) | engine default text ink | `--dg-ink` #242b36 / #e4e9f0 | n/a (engine) |
| P36.2 | `text[fill="#666666"]`, `[fill="#686868"]` (92) | #666666: CDG 147 and Differ 129 arrow FontColor; #686868: `NotePalette.HeaderInk` (computed, `NotePalette.cs:21`) | `--dg-ink-soft` #5b6474 / #aab3c0 | 686868: 9 lines (E2E `HeadersDetailsInterferenceTests` 6, UT `NotePaletteTests` 3) |
| P36.3 | stroke #181818 (93), `polygon[fill="#181818"]` (94) | engine default lines and arrowheads | `--dg-line` #4a5361 / #8792a3 | n/a |
| P36.4 | fill #e2e2f0 (95) | engine default participant fill | `--dg-participant` #e9eef6 / #253041 | n/a |
| P36.5 | path/polygon fill #feffdd (96) | engine default note; `NotePalette.DefaultNoteFill` (7) | `--dg-note` #fdf6e4 / #2b2618, edge `--dg-note-edge` #b9a57a / #75653f | 1 (E2E `NoteHeaderContrastTests`) |
| P36.6 | fill #cfecf7 (97) | `.eventNote BackgroundColor #cfecf7` PUC 1273; `NotePalette.EventNoteFill` (10) | `--dg-event` #e2eff9 / #16283b, round 17 item 4: #eee1f7 / #2a2140; edge #7fa6d0 / #44709c, then #b48cc8 / #7d5c9c; card edge #cfb6e6 / #5d4780 (`c-round17.css:79-80`) | 2 (UT `PlantUmlCreatorTests:818`, E2E `NoteHeaderContrastTests`) |
| P36.7 | fill #d4edda (98) | `Track.PassColor` (`Track.cs:16`, assertion `hnote` 365) | `--dg-pass` #e3f1e8 / #15301f, edge #8cbf9f / #33724d | 7 (UT `TrackThatTests` 4, `ParameterizedGroupRenderTests` 2, `TrackThatIntegrationTests` 1) |
| P36.8 | fill #f8d7da (99) | `Track.FailColor` (17) | `--dg-fail` #fbe8e6 / #3b1d1b, edge #dc9f97 / #7c3b34 | 3 (UT `TrackThatTests` 2, `TrackValueResolutionTests` 1) |
| P36.9 | `polygon[fill="#000000"]` (100) | step bar `#black` (`StepBarPlantUml.cs:55`), `.stepBody BackgroundColor black` (PUC 1257), Test bar (`TrackingDiagramOverride.cs:88`) | `--dg-bar` #2f3b4f / #3d4d69 | many (#000000 appears in 17 assert lines in 7 files, not all diagram) |
| P36.10 | `rect[fill="#f0f4ff"]` (101) | `skinparam ActivityBackgroundColor #f0f4ff` (`InternalFlowRenderer.cs:20, 77`) | `--dg-activity` #eef3fc / #1d2638 | 0 |
| P36.11 | stroke #666666 (102) | `ActivityBorderColor #666` (`InternalFlowRenderer.cs:21, 78`) and also component arrows `Color #666666` (CDG 146, Differ 128) | `--dg-activity-edge` #5f6878 / #7d899b | 1 |
| P36.12 | stroke #cccccc (103) | `SwimlaneBorderColor #ccc` (`InternalFlowRenderer.cs:22, 79`) | `--dg-lane` #c9d1de / #3a4656 | 0 |
| P36.13 | fill and stroke #438dd5 (104-105) | `DependencyPalette` HttpApi (48); `rectangle<<system>>` BackgroundColor and StereotypeFontColor (CDG 123, 127; Differ 104, 108) | `--dg-http` #438dd5 / #6aa5e4 | 32 (UT `PlantUmlCreatorTests` 27, `ComponentDiagramGeneratorTests` 4, `ComponentDiagramReportTests` 1) |
| P36.14 | fill and stroke #e74c3c (106-107) | see P35 | `--dg-db`: c-round5 #e74c3c / #f07a6c, then round 17: #0e7c86 / #45b3bf | see P35 |
| P36.15 | fill and stroke #9b59b6 (108-109) | `DependencyPalette` MessageQueue (51); `skinparam queue BackgroundColor` (CDG 141) | `--dg-msg` #9b59b6 / #c18fdc | 6 (UT `PlantUmlCreatorTests` 5, `ComponentDiagramGeneratorTests` 1) |
| P36.16 | fill #08427b (110) | `rectangle<<person>>` BackgroundColor and StereotypeFontColor (CDG 115, 119; Differ 96, 100) | `--dg-person` #08427b / #2f6db0 | 1 (UT `ComponentDiagramGeneratorTests`) |
| P36.17 | `.note-toggle-icon rect / line / text` (112-114) | CNS `createNoteButtons` literals '#ffffff', '#999', '#666' | `--dg-icon` #ffffff / #222b38, `--dg-icon-edge` #8a94a5 / #7d899b, `--dg-icon-ink` #5b6474 / #c5cdd8; polygons too (`c-round17.css:43`) | JS, none |
| P36.18 | rect/path/ellipse fill #438dd5 (c-round7 99) | component system box | `--dg-http-fill` #2b6cb0 (both; white name 5.42:1, was 3.49:1) | as P36.13 |
| P36.19 | rect/path/ellipse fill #e74c3c (c-round7 100) | component database box | `--dg-db-fill` #c0392b, then round 17: #0b6c75 (both) | see P35 |
| P36.20 | rect/path/ellipse fill #9b59b6 (c-round7 101) | component queue box | `--dg-msg-fill` #8e44ad (both; white 5.87:1, was 4.67:1) | as P36.15 |
| P36.21 | stroke #c0392b in `.component-diagram-section` (c-round17 121) | `skinparam database BorderColor #C0392B` (CDG 133) | `--dg-db-edge` #09616a / #2f8f99 | 0 asserts |
| P36.22 | `text[data-iflow-fill][text-decoration="underline"]` (c-round17 5) | the engine's #0000FF link colour, which BRS `bindIflowLinks` restores on hover (1325-1391; `isLinkFill` 1331) | `--kron-accent` | stays CSS: `bindIflowLinks` finds links by #0000ff, so a C# `hyperlinkColor` would break detection |

**Ownership.** Light-theme values can be written by C# (one source of truth, exports and NodeJs/Server/Local renders look
right). The dark theme still needs CSS keyed on something stable: the role classes (P24-P28) or a literal unique to its role.
Aliasing found: #666666 stroke is both an activity border and a component arrow; `polygon[fill="#000000"]` covers step bars
and the Test bar; #e74c3c covers database arrows and boxes; user `DependencyColors` and `PlantUmlTheme` change literals CSS
cannot know.

### P37. Literals the emitter writes that no mock rule remaps (unchanged in the dark theme)
Cache #F39C12 (`DependencyPalette.cs:50`; `skinparam collections` #F39C12 / #D68910, CDG 135-139; white name 2.19:1, fails AA
like the boxes c-round7 fixed), Storage #2ECC71, AI #16A085, User #7D3C98 (also the queue BorderColor), Unknown #95A5A6, the
person and system borders #073B6F / #3C7FC0, component `FontColor #FFFFFF`, the P95 hotspot colours #Green/#Orange/#Red (CDG
264-266), the diff diagram's #LimeGreen/#Red/#2E8B57/#B22222 (`ComponentDiagramDiffer.cs:112-122, 158, 197`), the render-error
marker #ffdddd (`DefaultDiagramsFetcher.cs:79`) and the Row marker #lightyellow (`TabularInputs.cs:85`). Outside diagrams,
`c-round2.css:7-10` remaps the run ring's strokes #1daf26 / #cc0000 / #949494 / #2e7bff.

---

## 6. Kronikol4J impact

Copies live in `kronikol4j-report/src/main/resources/io/kronikol/report/assets/` (36 files). Its CNS copy is far behind
(1695 lines against 3097): `createNoteButtons` has 11 parameters (no format or width buttons, 225), the replay still reads
`if (!owner._databasesVisible) …` (556), there is no `composeNoteSource` (its chain is inline at 1226). Every change is a new
divergence to record in the ledger: `C:\Code\Kronikol4J\docs\REMAINING_PARITY.md`, section
"## .NET-side features shipped after this audit (divergence ledger)" (1877).

| File | Items that diverge |
|---|---|
| `…/assets/collapsible-notes-script.js` | P1 (anchor matches its copy), P4-P10, P15 (only B1, B2, B4-B7 have a counterpart; no format/width buttons) |
| `…/assets/context-menu-script.js` | P13 (same 9 sites, 525-576) |
| `…/assets/flame-chart-render-script.js` | P14 (same `buildBar`, 5-11) |
| `…/assets/report-collapse-expand-all-function.js` | P33 (identical to .NET) |
| `kronikol4j-report/src/main/java/io/kronikol/report/DotNetHtmlReportRenderer.java` | P33 (buttons, 819); P11 (filter buttons 264-272, 829, 1096). P2, P3: no selects in the port |
| `kronikol4j-diagram/src/main/java/io/kronikol/diagram/plantuml/DependencyPalette.java` | P35 (`DATABASE("database", "#E74C3C")`, 21); P20 (`HTTP_API("entity", …)`, 20) |
| `kronikol4j-diagram/src/main/java/io/kronikol/diagram/component/ComponentDiagramGenerator.java` | P35 (`skinparam database`, 275-278); P17 if the component font moves to C# |
| `kronikol4j-diagram/src/main/java/io/kronikol/diagram/plantuml/PlantUmlCreator.java` | P17-P19 if the prefix changes (pragma 131, eventNote style 37) |
| `kronikol4j-report/src/main/java/io/kronikol/report/step/StepCollector.java` | P21 if rnote moves to C# (84) |
| `kronikol4j-core/.../tracking/Track.java` (218-219), `kronikol4j-report/.../flow/InternalFlowRenderer.java` (51, 104) | only if the pass/fail or activity literals change in C# |
| Tests and fixtures | Java tests asserting #E74C3C: `AwsTrackingTest`, `AzureTrackingTest`, `CassandraTrackingTest`, `ElasticsearchTrackingTest`, `GcpTrackingTest`, `SqlTrackingTest` (2), `MongoTrackingTest`, `ComponentDiagramGeneratorTest`; parity fixtures `kronikol4j-diagram/src/test/resources/parity/component.puml`, `fan-out.puml`, `participant-colors-fanout.puml`; 42 of 59 `kronikol4j-report/src/test/resources/parity/report-*.html` embed `createNoteButtons`; `parity-harness/dotnet-capture/fixtures/` (.NET captures plus `assets/` copies of 10 scripts and sheets) need recapturing |

---

## 7. Findings (do not port as-is)

- **F1.** `build.js` anchors `compose` (P6) and `replay-events` (P8) match 0 times on v5 (4.26.8 `filterShown`): the build
  throws on a v5 report.
- **F2.** `kronFlag` (`window[name] !== false`), `buildKronQueue` (`container[flag] !== false`) and `kronExactNoteMap` (raw
  `owner._…Visible`) do not resolve a missing flag the way `filterShown` does (CNS 1708-1713): the bug class 4.26.8 fixed.
  It shows once the Events/Setup defaults become configurable.
- **F3.** `noteKinds` (`c-diagram-look.js:63`, `^note(?:<<([A-Za-z]+)>>)? (?:left|right|over)`) accepts one stereotype.
  `applyNoteAppearance` stamps `note<<kronNoteWide>><<eventNote>> right` (CNS 1688-1696), which it skips; the count then
  differs from the drawn notes and every event card in that diagram loses its colour (read from code).
- **F4.** `keepPlace` reads `container._noteRendering` and `container.querySelector('svg')` on the element passed to
  `createNoteButtons`. For a fragment that is the `.puml-fragment`: the flag is set on the owner (CNS 1945) and the fragment
  is replaced on re-render, so it waits 10 s and never scrolls (read from code).
- **F5.** Two toggle models: Events/Setup keep a scenario's choice on `details.scenario` for diagrams drawn later; Assertions,
  Steps and Databases take the report's value in `_preProcessSource` (2410-2412). Pick one for all five.
- **F6 (product bug, latent, not fixed).** The greedy identical-text note map (CNS 783-797) mis-maps when a removed note's text
  equals a later kept note's and sits between the previous kept note and it (originals X, A removed, A kept; filtered X, A:
  the kept A maps to the removed one). It applies to Databases, Assertions and Steps today; P9's marked map fixes it.
- **F7.** P14 leaves `hue`, `lightness` and `depth` dead in `buildBar`; the C# `InternalFlowRenderer` flame renderer keeps
  the old hues unless ported.
- **F8.** After P15, `glyphButton`'s `slot` parameter and the format `slot` variable are unused.
- **F9.** Exports carry no page CSS (CMS `serializeSvg` 74-79, `svgToCanvas` 121-136): CSS-only recolours and role colours are
  lost in Copy/Save/Open as SVG or PNG, while the role pass's geometry (stretched strips, card paths, step-table rects) is
  kept. Its new rects (`k-step-table`, `k-step-table-head`, `k-badge`) have no fill attribute, so they export black.
- **F10.** The look applies only to BrowserJs diagrams (`[data-k-sequence]`, set by the render wrapper; selectors scoped to
  `.plantuml-browser`, `.iflow-diagram`). NodeJs inline SVG and Server/Local images keep the old look.
- **F11.** P11 adds Events/Setup buttons only beside an existing Databases button: a report with queues and no database gets
  no Events button.
- **F12.** `__kOpenText` titles every text tab "Diagram source", including "Open box text in new tab".
- **F13.** `c-round17-buttons.js:411-420` duplicates the "Fragment note indexing" comment in the built page.
- **F14.** The mock reverses two recorded product decisions without saying so: the format select without optgroup (RG
  1450-1457) and the blank line under step-bar tables (`StepBarPlantUml.cs:24-27`, pinned by E2E `StepTableBarTests.cs:90`).
- **F15 (test rule).** E2E `DiagramNotePlaywrightBase.cs:142` calls `WaitForFunctionAsync` without `PollingInterval = 200`
  (CLAUDE.md rule); it is in the glyph helper P15 forces to be rewritten.
- **F16.** `tests/TestTrackingDiagrams.Tests.EndToEnd/` holds two tracked files (`DiagramNotePlaywrightBase.cs`,
  `ReportTestHelper.cs`) with no project file: stale copies, not built. They also carry #E74C3C and the glyph locators.

---

## 8. Not covered here

`c-dom.r5.js` and `c-round16.js` also redefine product globals (`filter_dependencies`, `update_url_hash`,
`filter_happy_paths`, `clear_all_filters`, `_setTruncateLines`, `_setReportDetails`, `_depModeDefault`, `_depMode`,
`applyVisibility`, `run_search_scenarios`); `c-round6/8/9/13/16` scripts and sheets, `c-layout.js` and the rest of
`c-skin.css` are layout and need their own inventory.
