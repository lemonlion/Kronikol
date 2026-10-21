# Research notes (LONG_NOTE_SCROLL_PLAN)

Every `file:line` is at `aeaaf3b7` (5.2.1, origin/main on 2026-10-21); paths under `src/Kronikol/` unless they say
otherwise. Short names: **CN** `Reports/collapsible-notes-script.js`, **RS** `Reports/plantuml-browser-render-script.js`,
**CM** `Reports/context-menu-script.js`, **IS** `Reports/inline-svg-styles.css`, **CNS** `Reports/collapsible-notes-styles.css`,
**KN** `Reports/report-keyboard-navigation-function.js`, **RG** `Reports/ReportGenerator.cs`, **DCM**
`Reports/DiagramContextMenu.cs`, **RTD** `Reports/ReportToggleDefaults.cs`, **RES** `Reports/ReportToggleDefaultsResolver.cs`,
**PC** `PlantUml/PlantUmlCreator.cs`, **RCO** `ReportConfigurationOptions.cs`, **RTH**
`tests/Kronikol.Tests.EndToEnd/ReportTestHelper.cs`. The map was read by four read-only agents; each line used by the plan
was re-read by hand, and the ones not re-read are marked (agent).

## 1. How a sequence diagram is drawn

1. The server writes an empty `<div class="plantuml-browser" data-diagram-type="plantuml">`, its source gzipped in
   `#puml-data` (RG:2557-2562, 2651-2656). The notes script is emitted for BrowserJs only (RG:1636).
2. `enqueueElement` (RS:1667-1685) decodes the source and calls `window._preProcessSource` (CN:2635-2695), which applies
   the filters (CN:2644), keeps the original as `el._noteOriginalSource` (CN:2650), seeds YAML and width defaults
   (CN:2660-2669), the per-note steps (CN:2680-2688) and returns `composeNoteSource(...)`, **except** when every note is
   expanded, shown as JSON, with headers shown and no width change: then it returns the filtered source as it is
   (CN:2690-2694). `data-plantuml` then holds the drawn source.
3. `renderFragments` (RS:1119-1142) splits it with `splitWithChunkedNotes` (RS:1059-1115): `chunkLargeNotes` (RS:957-1024)
   cuts every `note left/right` block (not `note over`, RS:971) longer than `_maxNoteChars = 15000` (RS:669) at line
   breaks, marks the pieces "..Continued From Previous Diagram.." / "..Continued On Next Diagram.." (RS:993-994), anchors
   continuation pieces `left|right of <participant>` (RS:997-998) and forces a cut `== __SPLIT_BOUNDARY__ ==` (RS:1006)
   between them; `splitDiagramSource` (RS:865-935) then cuts by estimated height, `_maxDiagramHeight` (RS:668, the
   option `BrowserFragmentMaxHeight`, 12,000 px, `Constants/TrackingDefaults.cs:65`) at 45 px per arrow and 18 px per
   note line (RS:670-671, `estimateUnitHeight` RS:839-862). Units (an arrow with its notes) are never cut.
4. `processQueue` / `shimRender` (RS:1348-1448, 572-597) draw each part; the engine writes one `<text>` per word and per
   space under `skinparam wrapWidth` (every generated sequence diagram has it, PC:1217).
5. After a draw (`onQueueItemDone`, RS:1360-1382): failure description, `_recordDrawnSource`, link binding,
   `_makeNotesCollapsible` (RS:1376), assertion tooltips, then the zoom button in a frame (RS:1378). The role pass
   (`tagRoles`, RS:1881-2096) runs from a page-wide observer; it rounds each note body and marks it `.k-card`, its fold
   `.k-fold` (hidden by IS:92), and excludes `.note-toggle-icon` and `.note-hover-rect` from what it measures (RS:1893-1897).
6. Redraws: per-note gestures go through `rerenderWithNoteStates` (CN:2036-2247, a split branch and a single branch);
   report and scenario toggles through `processRenderQueue` (CN:2813-3053), which defers decoration of diagrams far from
   the screen until they come near (CN:2721-2796, 5.1.1). Both compose with `composeNoteSource` (CN:1807-1813):
   `applyNoteFormats` -> `applyNoteAppearance` -> `buildSourceWithNoteStates` (CN:1915-1987) -> `applyFilters`. Notes keep
   their ORIGINAL index through the first three; filters come last.
7. Note states (CN:319-324): 0 collapsed (a one-line preview), 1 truncated (first `_truncateLines` lines and a `...`
   line, CN:1951-1952), 2 expanded. Defaults: Truncated (RES:17), 10 lines (RES:18).

## 2. Everything that reads or touches a drawn note

| Reader | Where | What a scroll box changes |
|---|---|---|
| `findNoteGroups` | CN:167-271 | takes the SVG's first `<g>`; a note is a run of consecutive paths plus following texts; non-text elements inside the note box are skipped (CN:210-223). A box inserted after the note's last `<text>`, inside that `<g>`, is skipped. |
| `makeNotesCollapsible` | CN:816-1073 | removes old `.note-toggle-icon` and `.note-hover-rect` first (CN:820-822); writes `paths[0]._noteIndices = {original, drawn}` (CN:991). Runs again on the same SVG after a failure, a width refine and a deferred decoration, so it must remove old boxes too. |
| Hover buttons | CN:701-719, 740-760 | listeners on the note's paths, texts, buttons and `.note-hover-rect`; buttons are appended to the SVG root after the main `<g>` (CN:763), so they stay above a box inside the `<g>`. Measured: the pointer over a box shows no button (measurements M2). |
| Double-click | CN:705-707, 757-759 | on paths and the hover rect only; texts select a word. |
| `paintedRowCount` | CN:290-301 | counts distinct `y` of the note's texts; the width button's eligibility (CN:1025). Hidden texts keep their `y`. |
| `getNoteBBox` | CN:273-285 | paths and texts; button placement, hover rect, menu hit test. Hidden texts keep their box. |
| Context menu hit test | CM:539-558 | by DOM containment, then by the note's box against the click point; a click on a box is found by position (M2). |
| Box text | CM:624-652, 656-678 | from the source (`_fullNoteText`, YAML lines when shown; `_currentNoteText` when the note is not expanded). |
| Copy Highlighted Text | CM:696-729, 414-446 | `selectedSvgText` returns null on non-SVG text, so a selection in a box falls back to `Selection.toString()` (keeps line breaks, M2), then maps onto the note's text. |
| Image copies | CM:85-139 (`lookClone`), 167-238 | clone the whole SVG; computed `visibility:hidden` is inlined for painted tags (CM:120), so hidden rows stay hidden in a copy; a `<foreignObject>` gets no inlined style. |
| Source copies | CM:756-769, 818-834 | "current" is `data-plantuml` (the drawn source); "full" is `_noteOriginalSource`. |
| Selection | CM:912-945 | any click inside a diagram toggles `diagram-selected` (CM:930-937) on the diagram, or from 5.2.2 on the part under the pointer (`zoomBoxOf`, CM:25-30). |
| Ctrl+wheel zoom | CM:1163-1177 | needs a slider; works over a box (M3). A plain wheel has no script handler. |
| Drag-to-pan | CM:1181-1218 | `mousedown` in a `diagram-natural-size` container pans and `preventDefault`s; a drag in a box pans and selects nothing (M3). 5.2.2 rewrote this block (`zoomBoxOf`, `ownSlider`, a click dropped after a drag that moved more than 3 px); these lines are 5.2.2's. |
| Keyboard navigation | KN:1-35 | returns for INPUT, TEXTAREA, SELECT and tab lists; otherwise ArrowUp/ArrowDown move between scenarios with `preventDefault` (KN:16-31). Two more handlers act on ArrowUp/ArrowDown from ANY target outside a tab list (unless an input, select or textarea has the focus), both registered in the capture phase: `report-tabs-script.js:71-72` goes to the Features tab (`toFeatures`), and `report-features-tools-script.js:204-209` switches the Features tab to its Full view (`go('full')`). A box's own keydown handler runs after them and cannot stop them, so each of the three needs an early return for a target inside `.note-scroll`. |
| Role pass | RS:1881-2096 | reads every element of the main `<g>` except `g, a, title, tspan, defs` and the two note decorations (RS:1893-1897); a box must join the exclusions. |
| Link binder | RS:1482-1511, 1626-1655 | walks SVG `<text>`; a box holds none. |
| Search | `report-search-index.js:324-346, 465-500` | reads sources, never the drawn SVG; nothing highlights inside diagrams. |
| Export Filtered HTML | `report-export-function.js:90-113` | empties every drawn `.plantuml-browser`; a box never reaches an export. |
| Print | none | no `@media print` rule and no `beforeprint` handler anywhere. |
| SVG cache | CN:1989, 2103-2119, 2164-2201 | stores the engine's markup before decoration; restored diagrams are decorated again. Never evicted (an observation, plan Q11). |

## 3. Where a new note setting is threaded (model: `TruncateLines`)

`RTD:21` (public, nullable) -> `RES:18` (built-in) -> `RES:71-100` `Overlay` (enums go through `Defined`, RES:107-113; no
int range check exists; model: `PlantUml/DiagramWidth.cs:63-69`) -> `DCM:146-162` script tokens -> `CN:2278-2291` page
globals -> the toolbar's `BuildDetailsRadio` (RG:1079-1095, report toolbar RG:2040-2061, scenario gear RG:1163-1185).
Both reports resolve it (RG:531 Specifications, RG:536 test run); `MergeableReportRenderer.cs:116-119` resolves from the
options it is given, and `kronikol merge` gives a fresh `ReportConfigurationOptions` (`src/Kronikol.Tool/MergeCommand.cs:138`,
so a configured default never reaches a merged report, as for every toggle). `kronikol ingest` has `--headers`
(`IngestCommand.cs:169-177, 413-414`) and `--note-format` as models; no truncation flag. Pins: `ToggleDefaultsBaselineTests`
(unset = built-in, byte for byte), `ToggleDefaultsMarkupTests.cs:81-104` (script tokens and built-in globals),
`ReportToggleDefaultsResolverTests.cs:22-60`, `ReportConfigurationOptionsDefaultsTests.cs:49-60` (reflection over the
groups). `TruncateLineCount` (`Reports/TruncateLineCount.cs:13-43`) says off-preset values are unrepresentable by design.

## 4. Renderers that never run the page scripts

`PC:1135-1195` chunks RESPONSE notes over `MaxResponseNoteChunkLength = 15_000` (PC:39) into separate diagrams when
`!clientSideSplitting` (PC:1149): the Server, Local and NodeJs modes and the CI summary (`DefaultDiagramsFetcher.cs:253,
293-309`). Request notes there are cut to N lines (PC:1127-1133). None of them includes the notes script (RG:1636), so
none can show a scroll box; all keep today's chunking.

## 5. Tests the plan moves or must keep green

Facts that expand a note over 40 body lines, read its drawn rows, or depend on the 15,000-character chunks (agent
inventory, the line counts of the fixtures re-read where marked):

| Class | Facts | Why it moves |
|---|---|---|
| `ContinuationNoteInteractivityTests` | :195, :265, :432, :469, :523, :584, :667, :692, :737 | toolbar Expanded, then waits for 2+ parts of a 262- and a 502-line note |
| `ThreeFragmentContinuationTests` | :40, :56, :92, :108, :145 | 509-line note, Expanded, 3+ parts |
| `CapturedTextEscapeTests` | :236, :260 | a 1,282-line note starting expanded, more than one part, rows 0/100/200/319 drawn |
| `NoteButtonLayoutTests` | :217, :239 | the top-right buttons follow a tall note; `truncate-bottom` on a note drawn in parts |
| `DiagramNoteContentTests` | :29 | `<text>` count of an expanded 45-line note |
| `YamlNoteCopyTextTests` | :96, :114, :138 | a 52-line YAML view; highlight-copy of every drawn row |
| `ToggleDefaultsTests` | :49, :66 | counts 45 note lines in `data-plantuml` |
| `NoteYamlToggleTests` | :366 | a 49-line YAML view stays expanded |
| `NoteButtonsAfterHeaderHideTests` | :101 to :308 | a 45-line body (RTH:1183-1232), geometry of its buttons |
| `DiagramNoteLongNoteTests` | :9 to :259 | a 45-line note (RTH:684-730) |
| `DiagramNoteSplitTests`, `DiagramNotePartitionTests` | | 54/52- and 50-line notes |
| `NoteExpandArrowHeaderHiddenTests` | :53, :103, :120 | 44 lines, 38 body rows with headers hidden (under 40: stays) |
| `LargeNoteSplitTests`, `ColoredArrowSplitTests`, `CapturedTextSplitTests` | | call the splitter directly with 500 to 1,200-line notes: unchanged (the splitter is unchanged) |
| `BrowserRenderWorkerTests` | :714 | a 60,000-character ONE-line note: one body line, never capped |

`ReportTestHelper.ClassicStart` (RTH:25-30) pins the 4.x start states for most of these fixtures (truncated at 40).

Unit pins a change must respect: `DiagramContextMenuTests` (`GetFunction` slices at the next top-level `function` or
`window.`, :157-170; pins inside `buildSourceWithNoteStates` :175-204, the box-text menu :1311-1368);
`ScenarioTruncateAndCollapsibleNotesTests` (signature of `buildSourceWithNoteStates` :63-69; the literal first argument
of `isLongNote` in both queues :77, :172, :180, which F1's fix moves); `NoteAppearanceScriptTests` :41 (exactly four
copies of the note-header pattern) and :42 (its `\s*$` variant present); `NoteCopyFidelityScriptTests` :35 (seven
`noteLinesToText(`) and :47 (one copy of the painted-text expression); `ReportSheetColoursTests` :86 (no literal colour in
`collapsible-notes-styles.css`); `StylesheetRulesTests` :80, :100 (one phone breakpoint); `VerdictFencesTests` :60-77 (counts
the embedded scripts that carry `$verdict` fences, four; a script without fences, new or changed, leaves the count as it
is, so the plan's edits do not touch it); `ControlTitlesTests` :644 (every control has a title). New E2E classes must use
`TwoAnimationFramesAsync` (`AnimationFrameWaitTests` :37) and no direct `NewPageAsync`/`NewContextAsync`
(`BrowserStallTests` :56). CI lanes pick E2E classes by class-name substring (`.github/workflows/ci.yml:112-136`); a
class named in no lane runs in "E2E (Remainder)".

## 6. Documentation

Wiki at `5c783cd`: `Large-Response-and-Diagram-Handling.md` :10 (the 15,000-character split), :11 (12,000 px), :22 (a
note split across parts is one note); `PlantUML-Browser-Rendering.md` :62, :72, :136-146 (toggles on a large report), :199
(format and truncation count displayed lines), :211-264 (note width); `Generated-Reports.md` :777-790 (Payloads),
:918-928 (start states), :1392-1441 (`GenerateHtmlReport`); `Report-Configuration.md` :138, :226-243, :249-251;
`CI-Summary-Integration.md` :148-157; `_Sidebar.md` :112-115, :139. In the repo: RCO:621-628 (the toggle-default groups'
summary), RTD's member docs, the Expanded control's title "…show in full" (RG:1091; pinned by
`ToggleDefaultsMarkupTests` :175, :606, `ScenarioToolbarTests` :142, `ControlTitlesTests` :291, :299, `ReportToggleTests`
:442).

## 7. Kronikol4J

The port ships 3.0.43's page scripts (`kronikol4j-report/src/main/resources/io/kronikol/report/assets/`, not byte copies;
`README.md:49-50, 74`), with truncation hard-coded (`collapsible-notes-script.js:974-976` there). The divergence ledger is
`docs/REMAINING_PARITY.md` on the port's `origin/main` (`8aa6c52`; the local checkout is behind): one bullet per .NET
release, a bold title "(.NET x.y.z, date, `PLAN.md`)", a verdict ("Not mirrored", "Worth porting"), then details.
