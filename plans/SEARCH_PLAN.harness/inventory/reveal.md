# Going to the match: what the page has today (2026-10-26, main 37428293, 5.9.0)

Read-only deep dives behind SEARCH_PLAN 4.6.5 (A9). Lines are at `37428293`. Short names: CN
`collapsible-notes-script.js`, PR `plantuml-browser-render-script.js`, CM `context-menu-script.js`, NAV
`report-navigation-script.js`, UH `report-url-hash-function.js`, SF `report-search-function.js`, SI
`report-search-index.js`, TG `toggle-script.js`, SR `report-select-row-function.js`, SS `report-sub-steps-on-expand.js`,
IF `internal-flow-popup-script.js`, FL `flame-chart-render-script.js`, RG `ReportGenerator.cs`, PC
`PlantUml/PlantUmlCreator.cs`, DW `PlantUml/DiagramWidth.cs`, SL `PlantUml/PlantUmlStatementLimits.cs` (all under
`src/Kronikol/Reports/` but the last three, under `src/Kronikol/`).

## Nothing knows where a search matched, and nothing marks text

- Deep search returns scenario indices only (SI:631-666); its verify pass is a boolean over joined text (SI:252-272).
  A query's terms are independent (`flour`, `:`, `200` may each be anywhere in the scenario, SF:386-405).
- No `<mark>`, `::highlight` or `CSS.highlights` anywhere in the report. The only flash: `toggle_table_ref`'s
  `.step-param-highlight`, 1.5 s (report-toggle-table-ref-function.js:25-33). Nothing targets anything inside a diagram.
- The single-match reveal (SF:19-29) opens feature, rule and scenario (recorded, closed by `close_opened_by_search`,
  SF:31-34) and the diagrams, steps and background sections (not recorded); it does not scroll.

## Text forms

- The generator writes JSON only, re-indented `"k": v` (PC:1579-1598), escaped, long runs cut with markers
  (PC:1736-1799). YAML is made in JS at draw time from notes that parse (CN:1228-1276, 1478-1484, 1679-1738); YAML is
  the default since 4.0.0 (ReportConfigurationOptions.cs:609-616). Deep search holds the JSON source only.
- Headers are drawn grey as `[Key=Value]` only with Headers on (PC:1549-1555, 1908-1918); always in the deep source.
- `data-search` has names, tags, the error, steps, participants and request paths (RG:2459-2474, 7104-7131): no
  payloads, no headers. With internal-flow tracking on (the default, ReportConfigurationOptions.cs:189) the label is
  `[[#iflow-ID GET: /path]]` (PC:389-390) and `UrlRegex` (RG:7107-7109) never matches it (B11).
- The engine draws each word and each space as its own `<text>` (`skinparam wrapWidth` always set, PC:1217; rows are
  the distinct `y`, CN:335-349; CM:589-594). U+200B marks Kronikol's own breaks (DW:109-136); `_stripZeroWidth` CN:96.
- The search normalizer lower-cases, strips tags, deletes `\n` escapes and the spaces after them, collapses spaces
  (SI:57-73): a match's offset needs a map back to the raw source.
- Cuts: arrow labels at 2000 characters, 350 with an internal-flow link; participants 200; loop and partition labels,
  coloured step bars, activity actions 600; table cells 60 (SL:215-290, DW:287-288). A cut path is repeated in the note's
  `[Full path]` block (PC:381-408, 473-483).

## Toggles (six: Headers, Assertions, Steps, Databases, Events, Setup; written only where the report has the content, RG:1581-1597, 2082-2092)

- Source marks: Assertions `hnote across <<assertionNote>>` (Tracking/Track.cs:366; filter CN:2973-2975); Steps
  `hnote across <<stepDelimiter>>` (StepBarPlantUml.cs:55-56; CN:2977-2979); Databases and Events: `database|collections`
  and `queue` participants, their arrows, the note after a removed arrow, `note … of alias`, activations (CN:2999-3112);
  Setup: the `partition … Setup … end` frame, or everything before the first When step bar (PC:213; CN:3141-3225);
  Headers: note lines starting `<color:#hex>`, dropped in `buildSourceWithNoteStates` (PC:1908-1918; CN:8, 2555, 2566).
- State: `window._xVisible` / `_headersHidden` (CN:2953-2961), on `details.scenario` (CN:4136-4144), on the
  `.plantuml-browser` container; container wins, then scenario, then window (CN:1904-1914). Headers has no scenario
  state (CN:4081-4100).
- Every change rewrites the source and redraws (`composeNoteSource`: YAML swap, width, note states and headers, the
  40-line cap, the filters, CN:2375-2390; `processRenderQueue` CN:3568-3880; a picture whose source equals the drawn one
  is skipped, CN:3657-3667).
- One scenario: `_toggleScenario{Assertions,Steps,Databases,Events,Setup,Headers}(btn)` flip relative to the button
  (CN:4147-4156, 4093-4100); no setter. One diagram: no UI, no setter; a container state is overwritten at first draw
  (CN:3325-3329).
- First draw reads the scenario's five filter choices (`scenarioChoice`) but the report's payload state
  (`window._detailsDefault`) and headers (`window._headersHidden`) (CN:3322-3376): a gear choice of Payloads or
  Headers never reaches the scenario's diagrams drawn after it (B12).

## Notes

- Per-note state `owner._noteSteps[i]` (0 collapsed, 1 truncated, 2 expanded); `i` counts `note … left|right` blocks
  only (`parseNoteBlocks`, CN:100-118); the owner is the `.plantuml-browser` even in parts (CN:879). The first draw resets
  every state (CN:3364-3374). `setNoteState` is private and does nothing while any render runs (CN:2612-2619).
- Collapsed: a 60-character preview from the JSON even in YAML mode (CN:135-156, 2526, 2666) (B13).
- Drawn note to source: `makeNotesCollapsible` (CN:868-1137) lines up `findNoteGroups` (CN:206-319) with the source
  blocks, through parts (`computeGlobalNoteIndex`, CN:837-866), filtered notes (CN:916-936) and empty notes
  (CN:1003-1031), and stores `_noteIndices = {original, drawn}` on the group's first path (CN:1052); read by
  `_noteIndicesOf` (CN:2921-2926; the note menu since 4.26.8, CM:800-811).
- The scroll box (PayloadScrollLines, 40): `capLongNotes` (CN:1925-2110); built by `watchScrollBox` within one window
  height (CN:2174-2238) as `foreignObject.note-scroll-frame[data-note=i] > div.note-scroll`, rows under it
  `visibility: hidden`; its text node layout CN:2136-2165; a Truncated note's box holds only the truncated lines; every
  decoration rebuilds it. `owner._noteScrollTop[i]` is restored (CN:2326-2328).

## Messages, parts, drawing

- The engine writes `data-source-line` (0-based, `@startuml` is 0) on `g.message#msgN` and participant groups
  (`plans/WASM_ENGINE_PLAN.md:85-87`); each message has an empty twin (select `:not(:empty)`); notes get none. The lines
  count the text actually drawn: after `composeNoteSource`, the split into parts and each part's added lines (PR:1220-1226,
  1453-1457).
- Lazy drawing: an IntersectionObserver with a 200 px margin (PR:2177-2186, 2273); `_renderDiagramsInContainer(c,
  shownOnly)` queues descendants only (PR:2257-2272). No event or promise when a diagram is drawn; signals:
  `data-rendered` (set on a parted host before its parts draw, PR:1542), `svg[data-k-roles]` (PR:2405, 2621-2628),
  `_noteRendering` (CN:3506-3527), `_renderCompleteCount` (CN:3624), `_renderWithPending(q, els, done)` (CN:3973-3981).
- Parts: `.puml-fragment#<id>-frag-<n>` (PR:1544-1552); redraws swap in `.puml-fragment-new` (CN:2688-2741); zoom kept
  by `_zoomState` (CM:1789-1814); zoomed in, the box scrolls sideways (CM:1404-1409).
- The engine's ids (`msgN`, `partN-head`) repeat in every diagram, written in unchanged (PR:213-220), and NAV's
  `address()` saves an element by its id and its index among the page's matches (NAV:91-107) (B10).

## The rest of a scenario

- Views: `details.example-diagrams` with `seq`, `activity`, `flame` (RG:1236-1260); switch by clicking
  `.diagram-toggle-btn[data-dtype]` (TG:22-48). Full view: `kronikol_set_features_view` (report-features-tools-script.js:33).
- Steps: `details.scenario-steps` (RG:4182), `details.scenario-background` (RG:4139), composite `details.step-collapsible`
  (RG:4274-4276), leaf `div.step` (RG:4284-4452), held sub-steps `div.sub-steps[data-steps]` (RG:4484) drawn on first
  open, then `kron-steps-drawn` (SS:17-43); long values behind `button.step-table-ref` (`toggle_table_ref`). Steps have no
  ids; the query tool's step addresses are `2.1`, `b0` (`src/Kronikol/Query/Address.cs:11-13`).
- Failure: `details.failure-result[open] > pre` (RG:2519-2529).
- Outlines: `details.scenario-parameterized` (RG:3598); rows `tr[data-row-idx][data-stable-id][data-row-search]`
  (RG:3685, 3793); `selectRow(tr, 'pgrpN')` (SR:1-68); complex cells `details.param-expand`. The search item is the whole
  group; which row matched is lost.
- Whole-test flow: `.diagram-view-activity`, `.diagram-view-flame > .iflow-flame[data-flame-z]` (RG:2643-2648); its
  corpus piece is sources, span names, marker labels, one per line (SI:276-284). The per-call internal-flow popup's text
  is not in the corpus (SI:511-546).
- Server, local and Node renderers draw `<img>`; their text is only in a closed "Raw PlantUML" `<details>` (RG:2618-2631);
  an engine failure shows `.engine-failure` with the source in the same kind of section (PR:1726-1790).

## Navigation

- A place is `{tab, view, anchor}`, the address `#[anchor&][tab=][view=]filters` (NAV:49-84); every other `k=v` is read
  as a filter (NAV:57-58). An entry `{v, id, kind, key, scroll, focus, layer}` (NAV:299). `#sid-` clicks become
  `go({anchor})` (NAV:617-628); `arrive` (NAV:459-531): layer, tab and view, filters, scroll (`restore` and `settle`,
  NAV:186-233, or `reveal_url_anchor`), focus, status line. `say()` is private (NAV:402-413). `jump_into_view` re-aims for
  ten frames and cannot be stopped (UH:110-129).
