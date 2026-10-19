# OPTION_C_PLAN: the Option C report in the product, for 5.0.0

**Status: IN PROGRESS on the local branch `v5` (not pushed, not released).** Green-lit by the owner on 2026-10-18,
after round 17 of the preview: "when you're done, put all of this in an (unreleased) V5 branch. I plan to put it
together with the size reduction stuff for a release. If this is all just scratchpad stuff then when you're done
make sure it's all done properly in the branch ready to be released (with the size reduction stuff)."

5.0.0 is therefore three pieces of work on one branch: this plan, the dependency filter's OR default (`9452ddb5`,
already on `v5`, under `[Unreleased]` in the changelog), and the report size reduction (`REPORT_FORMAT_PLAN.md`,
written in its own worktree, `plan/report-format`). The version moves to 5.0.0 when the branch is released, not
before; until then the changelog collects its entries under `[Unreleased]`.

## 1. What "Option C" is, and where its specification lives

Option C is the quiet-bar redesign of the whole report that `TOOLBAR_REDESIGN_PLAN.md` set out on 2026-08-31 (for
v4, then moved to v5 by `ROADMAP.md` D30). Between 2026-10-16 and 2026-10-18 it was built as a preview over a real
report (BreakfastProvider's ReqNRoll suite, 214 scenarios) and refined over 17 rounds of the owner's notes, plus two
notes after round 17 (items 24 and 25 below). The preview is the specification: where this plan and
`TOOLBAR_REDESIGN_PLAN.md` disagree, the preview wins, because every difference is a later decision of the owner's.

The preview applied its changes to a report written by 4.26.4, in the browser:

- a stylesheet composed by `compose.py` from the August design layers (`aug/`) and fourteen round files, with the
  colour literals replaced by `--kron-*` tokens and the diagrams' colours by `--dg-*` tokens;
- scripts run at load that build or rearrange the page (`c-layout.js`, `c-dom.r5.js`, `c-round*.js`) and
  post-process each drawn diagram (`c-diagram-look.js`);
- text patches to the report's own scripts made at build time (`build.js`, with `c-round16-*.js` and
  `c-round17-buttons.js`).

`OPTION_C_PLAN.harness/` holds:

- `mock/`: those sources as of round 17 plus items 24 and 25. `c-skin.css` and `c-dom.js` are generated
  (`compose.py`, `build-r9.sh`); `c-round17-buttons.js` is generated from `r17-createNoteButtons.js` by
  `r17-make-buttons.py`, then `r17-keepplace.py` and `r24-smaller-top.py`, in that order.
- `checks/`: the probes and the regression suite the preview was measured with (`run-regression.sh` runs all of them).
- `inventory/`: three inventories of the preview, made on 2026-10-18 against `v5`, which map every change to the
  product: `stylesheet.md` (874 rules, the tokens with light and dark values, the hacks to drop), `structure-and-
  behaviour.md` (D1 to D50, the page the generator must emit, a 53-row tooltip table, test exposure, an order of
  work) and `script-patches-and-diagrams.md` (P1 to P37, F1 to F16: the patched report scripts, the note buttons, the
  diagram pass, emitter colours, Kronikol4J files). This plan cites their item numbers rather than repeating them.

## 2. What "done properly" means here

1. **The generator writes the page as it is meant to be.** Nothing is moved after load: the run band, the filter
   box, the bar with its tabs, rows and panels, the tools lines and every scenario section are emitted in their final
   place by `ReportGenerator.cs`. Moving them at load costs a visible shift on every open and leaves two structures to
   test (structure inventory, section 1, is the target tree).
2. **One stylesheet, built on tokens.** The `--kron-*` and `--dg-*` custom properties become the documented theming
   surface. The rules that existed only to beat the 4.x stylesheet (85 `.kron-c`-scoped rules, 33 `!important`, 16
   doubled classes, 29 `:is()` bumps) go, with the 4.x rules they beat. Estimate: 70 to 85 KB of CSS against 69 KB
   today, while about 66 KB of preview layout script becomes markup.
3. **Colours that mean something are set where the meaning is.** The diagram colours the preview remapped with CSS
   attribute selectors are changed in the C# emitters (the database colour, component box fills and borders, the
   donut, the step bar form, the diagram background), so a diagram drawn by Server, Local or Node rendering, or
   exported, has them too. What needs rendered geometry (the role pass: lifelines, bands, step tables, full-width
   strips, cards) stays a post-render script, because Teoz SVG carries no semantic classes.
4. **Behaviour lives in the report's scripts,** edited where the preview patched them, and new behaviour in new
   script resources. Patches written against 4.26.4 are re-derived against 4.26.9's code (`composeNoteSource`,
   `filterShown`), not replayed.
5. **Tests first.** Every owner request from the 17 rounds and items 24 and 25 that changes behaviour or something a
   reader sees gets a test that fails on 4.26.9 and passes on `v5`; UI behaviour gets Playwright tests (CLAUDE.md).
   Existing tests that pin the 4.x page are changed in the commit that changes what they pin, and say why.
6. **Parity with the preview is measured,** not eyeballed (section 6).
7. **The preview's own defects are not ported** (structure inventory section 6, patches inventory F1 to F16): for
   example Clear All leaving `dep:` / `duration:` / `is:` searches applied, round 17's drawn triangles losing their
   tooltips, widening one note dropping the event colour of every card in its diagram, keep-the-reader's-place not
   scrolling in a diagram drawn in fragments, and exported SVG and PNG losing the colours and drawing the step table
   shading black.
8. **Docs:** the wiki pages that show the report (and their screenshots and GIFs, `WikiScreenshotTests`,
   `WikiGifTests`), the README where it shows the report, the changelog's `[Unreleased]` section, and a Kronikol4J
   divergence ledger entry for every change to the generated page or to an emitter.

## 3. Owner's requests carried by the preview

Rounds 1 to 16 are recorded in the inventories against the code that carries them. The last batch, for reference:

| Item | Request | Where it lands |
|---|---|---|
| R17.1 | `dep:` and `duration:` must not clash with real searches | measured: 0 matches in 20 local reports (339 KB of page text, 2.0 MB of diagram sources); qualifier rules kept (D16) |
| R17.2 | collapse all without expanding first, no more room | `FEATURES` and `SCENARIOS` groups, expand and collapse icons, dimmed when idle (D22) |
| R17.3 | a heading in the payload format dropdown | `optgroup` "Payload format" (P-items, D30) |
| R17.4 | event notes must not clash with step strips | event cards lavender, from the message arrows' violet (difference 10.1 from the strips, was 2.9) |
| R17.5 | link hover in sequence diagrams in the scheme's colour | accent fill on hover |
| R17.6 | step-strip tables like the parameter table, no gap under them | header row shaded, trailing line dropped, 7 and 6 px padding |
| R17.7 | event colours lost | the kinds read from the drawn source; F-finding on two stereotypes fixed in the port |
| R17.8, R17.12 | Firefox 1px label offsets | measured fixes, re-measured on the native markup |
| R17.9 | collapsing a tall note loses the reader's place | the note's new bottom brought back to the click (and in fragmented diagrams) |
| R17.10 | flame chart colours | eight-hue palette, pastel fill with a 3px edge, 2px between rows |
| R17.11 | filters drop from the top of the floating bar | box at the top of the bar, Filters button only while the bar floats, home again at rest |
| R17.13, 14, 15, 24 | note buttons | drawn triangles 2px clear (the top-right toggle 8 x 10 px, 3px clear, item 24), packed with no gaps, a truncated note's pair |
| R17.16 | Firefox step indent | product rule specificity |
| R17.17 | expected green, actual red | error diff colours |
| R17.18 | steps align with the Steps heading | a drawn triangle in a fixed box, circle and text on one column |
| R17.19 | centred diagrams | `margin-inline: auto`, symmetric padding |
| R17.20 | clicking in and out of a diagram scrolls the page | the focus helper ignores pointer focus |
| R17.21 | Firefox: open the diagram image in a new tab | the tab opened during the click, the picture written into it |
| R17.22 | database red reads as an error | deep cyan `#0E7C86` in `DependencyPalette` and the component skin |
| R17.23, 26 | per-scenario settings hidden by default, opened per scenario | a gear at the top right of each scenario's diagram, grey until pointed at; it opens that scenario's bar, starting "This scenario:", with the gear at its right end in the same place; the open bar sticks under the report's bar while its diagrams are in view and steps aside on the Activity and Flame tabs; a phone keeps its title-line button; nothing report-wide (item 23's switch withdrawn); feature cards clip with `overflow: clip` so the sticky row works (D23) |
| R17.25 | toggles change width while loading | fixed padding, the sweep shown, labels reserve their bold width |
| R17.27 | a heading in the truncate dropdown | `optgroup` "Payload length" (the owner's pick over "Truncate at"), matching "Payload format" and "Payload width" |
| R17.28 | no fold controls on the Summary tab | the `FEATURES` and `SCENARIOS` groups and the divider before them hide in the Summary view |
| R17.29 | History pass rate as rings | one ring per run, drawn like the summary's (pass, fail, skipped, bypassed arcs), the rate inside; a run with a failure never reads 100% |
| R17.30 | History duration with its time | each bar carries its time ("44s", "2m 26s", "1h 5m", "850ms"), drawn as HTML so the label is not stretched |
| R17.31 | report bar controls out of line (Firefox) | the payloads group centres like the format group, and item 25's bold-width wrapper keeps the label's baseline (`inline-flex`), so buttons are 28.25 px like the dropdowns; text centres within 0.5 px in both browsers |
| R18.1 | "in this version, history and diagnostics is to be brought back, in those tabs (not as part of the rest of the report though)" (2026-10-18, during S3) | `ShowHistorySection` and `ShowReportDiagnosticsSection` default to `true`: the History and Diagnostics tabs appear whenever the run read history or recorded a diagnostic; `ShowScenarioHistory` (the scenarios' sparklines, pills and verdict attributes) stays off; `kronikol ingest` follows, with `--no-diagnostics-section` (S3) |

## 4. Stages

Each stage is one or more commits on `v5`, tests first, with the preview's checks run against the branch's
rendering of the parity run at the end of the stage. The order follows the structure inventory's section 7.

| Stage | Content | Needs |
|---|---|---|
| S0 | Parity harness: the parity run, its rendering by the branch, the preview rebuilt over the same run (section 6) | done 2026-10-18 |
| S1 | Stylesheet foundation, as several commits: token sheet (light, dark under `[data-theme]` and `prefers-color-scheme`), `[hidden]` rule, theme boot script, title line and theme switch (D1); then the surfaces no later stage restructures, rewritten to tokens and the C rules: page chrome, the features and scenarios list, the scenario header, the failure result and error diff, steps and parameter tables (stylesheet inventory 2, rows 0 and 9 to 13), so a dark page reads before the later stages land. The sticky bar moves to S3 with the nav it pins | Q1 |
| S2 | Run band and the filter box on its own row (D3, D4, D5) | S1 |
| S3 | The bar, its tabs and panels (D20); failure clusters and background calls placed (Q4); the tab options (Q2) | S2 |
| S4 | Features tools line (D21, D22, D35) and the report bar's zones (D30); the `ExpandFirstScenarioWithDiagram` helper (213 calls) moved to the new controls | S3 |
| S5 | Filter pipeline: item flags for tags and qualifiers, `clear_all_filters` covering qualifiers, URL hash v2 (Q5), the readout and its event (D14) | S3 |
| S6 | Filter rows: D6, D7, D13, D19, then tri-state Tags, Dependencies and Categories (D8 to D12, Q5), qualifiers and help (D16), chip placement (D17) | S5 |
| S7 | More filters (D15), filters in the bar (D24, item R17.11, with the pin line counted as floating), pinned state (D25), measured layout (D18) | S4, S6 |
| S8 | Scenario toolbars split (D29 to D34, D37), Scenario settings (D23, sequence-only toolbars included), loading state (item 25), label spans the toggle code updates in place | S4 |
| S9 | Note script: Events and Setup toggles on one filter model with the other three (D38, P4 to P11), note buttons (P15, items R17.13 to R17.15 and 24, tooltips kept), keep the reader's place (R17.9), dropdown headings (R17.3), the exact note map (F6) | S8 |
| S10 | Diagrams: emitter changes (fonts, geometry, `entity` to `participant`, the step table's trailing line, `#0E7C86` and the component database skin, the cache box's contrast, donut and background); the role pass in `plantuml-browser-render-script.js` (cards, bands, step tables with the two-stereotype fix); centring, link hover; exported SVG and PNG carrying the look (Q6) | S1 |
| S11 | Tab contents: Timeline (D27, minute-long rows rescaled), Components (D26, D28), History (D47), Diagnostics; flame chart palette (D44); internal-flow popup | S3, S10 |
| S12 | Behaviour and layout: focus helper (D48, R17.20), open in a new tab (D43, R17.21), steps alignment (R17.18), phone layout (one breakpoint rule, 768.02 px, not the preview's 769), titles audit against the 53-row table (D49) | all above |
| S13 | Specifications and labs pages: the token sheet and boot script, the violet theme as token overrides (Q3) | S1 |
| S14 | Docs (wiki pages, screenshots and GIFs, README), changelog `[Unreleased]`, Kronikol4J ledger entries; final parity run, the core suite and the full Playwright suite | all |

## 5. Kronikol4J

The Java port shares `stylesheets.css` byte for byte (`Constants/Stylesheets.cs` 27-30 on the .NET side) and keeps
its own copies of the report scripts in `kronikol4j-report/src/main/resources/io/kronikol/report/assets/`; its notes
script is far behind (1,695 lines against 3,097). The script-patches inventory, section 6, lists the 4 asset files, 5
Java emitter files, tests and parity fixtures this plan makes diverge. Each stage that changes the page or an emitter
adds an entry to the divergence ledger (`docs/REMAINING_PARITY.md`); porting Option C to Kronikol4J is a separate
decision of the owner's, not part of this plan.

## 6. Checks

- **Parity run.** `scratchpad/v5parity/history-runs-mergeable.sh` (copy in `checks/`) runs BreakfastProvider's ReqNRoll
  suite five times in the throwaway worktree `BreakfastProvider-cmock` (labelled as its CI runs #318 to #322, with
  the same three edits that give the History tab a flaky, a failing-since and a new failure), with
  `GenerateMergeableData` on. The branch draws the last run with
  `kronikol merge bp3-TestRunReport.json -o v5-<stage>.html -t "Breakfast Provider (ReqNRoll) Test Run Report"
  --history bp3-history.jsonl --no-json`; the preview is rebuilt over the same run
  (`INPUT=bp3-TestRunReport.html OUTPUT=option-c-bp3.html sh build-r9.sh`). Both pages then show the same content.
- **The preview's checks** (`checks/run-regression.sh`: diagram, note hover, diagram text contrast, functional,
  search qualifiers, toggles, layout at widths, tabs, navigation, focus) run against each stage's rendering, with
  selectors moved to the native markup where the markup changed; their results are compared with the preview's.
- **Screens.** Chrome and Firefox, light and dark, 1400 px and phone width, per surface, side by side with the
  preview.
- **Suites.** The core suite and the full Playwright suite pass at the end of every stage that touches the page; the
  release check (`release.slnf` in Release for every target) at the end.

## 7. Questions for the owner (asked 2026-10-18; work proceeds on the recommendation unless answered otherwise)

1. **Remembered display preferences.** Persistence in the browser was removed on 2026-04-08
   (`PersistentFilterStateReportTests` pins that the page never mentions `localStorage`). The theme switch wants to
   be remembered (the report-wide Scenario settings switch it was asked for alongside was withdrawn by R17.26: each
   scenario's gear opens its own bar and nothing is remembered). Recommendation: allow it for the theme only, never
   for filters or toggles, and narrow the test to say so.
2. **Options that name sections now tabs** (`ScenarioTimelineVisible`, `ComponentDiagramVisible`,
   `FeaturesSummaryOpen`, `DiagnosticsOpen`). Recommendation: keep the names; `...Visible` decides whether the tab
   exists and `...Open` which tab or view is showing when the page opens; documented for 5.0.0. As built in S3: the
   names are kept, and each keeps its 4.x meaning of "shown when the page opens": `ScenarioTimelineVisible` and
   `ComponentDiagramVisible` choose the tab the page opens on (the timeline when both are set, as before), and every
   tab exists whenever its section does. Read as written, the recommendation would have hidden the Timeline tab by
   default, since `ScenarioTimelineVisible` defaults to `false`. `DiagnosticsOpen` has nothing to open in a tab;
   `FeaturesSummaryOpen` opens the Features tab on its Summary view (S4).
3. **Specifications and labs pages.** Recommendation: the token sheet, the theme and the violet theme as token
   overrides, no layout redesign.
4. **Failure clusters and background calls.** Recommendation: at the top of the Features tab, as cards.
5. **Tri-state filters.** Recommendation: their state in the page's link (URL hash v2, old links still read), and
   Categories tri-state as Tags and Dependencies.
6. **Diagrams not drawn by the page** (Server, Local and Node rendering, exports). Recommendation: what can be set in
   the C# source is set there for every rendering; the role pass stays in the page; exported files carry its styles.
7. **Published events.** An Event Grid publish draws no payload card (publishes never do). Should published event
   bodies be drawn?
8. **The branch.** Pushed to origin as it grows, or kept local until the release.

## 8. Log

- 2026-10-18: branch `v5` made from `origin/main` at 4.26.9 (`354cb998`) in the worktree `Kronikol-v5`; the
  dependency filter's OR default cherry-picked onto it (`9452ddb5`). The local branch that held it was renamed from
  `v5/dependency-filter-any` to `v5-dependency-filter-any`, because git cannot hold `v5` and `v5/...` at once. The
  preview's sources, checks and the three inventories copied to `OPTION_C_PLAN.harness/`. S0 done: the parity run
  made (five runs, the last with two failures), the branch's `kronikol merge` of it written, and the preview rebuilt
  over it. Items 24 (the top-right triangle smaller) and 25 (no width change while loading) added to the preview
  after round 17.
- 2026-10-18: items 26 to 31 added to the preview and to section 3 (per-scenario gear, the lines dropdown heading,
  no fold controls on Summary, History rings and timed bars, the report bar's vertical alignment), each with a check
  in `checks/` and the whole regression set passing in Chrome and Firefox. Q1 narrowed to the theme. S1 widened to
  the base surfaces, which no stage covered (section 4).
- 2026-10-18, S1 commit 1: `report-tokens.css` embedded and opening the page's first `<style>`; the boot script
  (`report-theme-boot-script.js`) before it; the title line with the switch, and `report-theme-switch-script.js`
  after the page's other scripts; the page's ground, ink and type from tokens. The theme is the only thing a report
  stores (`PersistentFilterStateReportTests.Report_stores_nothing_but_the_theme`). Tests: core `ReportThemeTests`
  (6 failed before the change, on the missing markup, scripts and rules), Playwright `ThemeTests` (dark system,
  following the system, the switch kept over a reload and the system, a stored pick from the start).
- 2026-10-18, S1 commit 2: the base surfaces (stylesheet inventory 2, rows 0 and 9 to 13) rewritten in place in
  `stylesheets.css`: each 4.x rule takes the preview's values where the preview overrode it and keeps the rest, with
  every colour on a token; the 4.x phone rules the preview's desktop rules beat are removed. The diff's labels lose
  their colons (`ErrorDiffParser`). Measured with `checks/style-parity.js` (computed styles of 50 elements, the
  preview over the parity run against the branch's `kronikol merge` of it) and `checks/pseudo-parity.js` (the drawn
  marks): 524 differences before, 3 after, in Chrome light and dark, Firefox light, and Chrome at 700 and 400 px.
  The 3 are deliberate: the error diff's container takes the monospace token, where the preview kept the bare
  `monospace` keyword (no visible text is set in it). Also deliberate: the Rule label's 11 px from the type scale
  (the preview's round 7 missed it, 10.5 px); the 4.x phone rule that puts a scenario's endpoint on its own line is
  kept (the preview's desktop float beat it by accident; the parity run has no endpoint). Not in this stage: the
  diagrams section's summary (S8), the Summary table (S4), failure clusters and background calls (S3). Tests:
  Playwright `BaseSurfaceStyleTests` (cards in both themes, the failed cross, the diff's colours and labels in both
  themes, the steps' one column, the parameter table's border; all 7 failed on the S1 commit 1 stylesheet) and
  `ErrorDiffParserTests.GenerateDiffHtml_labels_the_rows_without_a_colon`. A preview defect found by
  `IngestedBypassTests` and not ported: the feature and scenario header rules name the ink and out-rank `.skipped`,
  so the preview drew a skipped feature or scenario like one that ran (4.x greyed both); the branch gives them
  `--kron-skip` again (`BaseSurfaceStyleTests.A_skipped_feature_and_scenario_keep_the_skipped_colour`, red before
  the rule). The Playwright tests that pinned 4.x colours or sizes this commit changes on purpose now say so and
  read the 5.0.0 values (row tints, the steps' radius, the doc string, the bypassed step's circle, the toolbar's
  hover and search mark); the scenario's diagram toolbar keeps its 16 px sides as a length, since 1 em is 14 px on
  the new type. 5.0.0's smaller type leaves the filtering box wider than its chips, help table and export buttons
  at every width, so `ReportSectionWidthTests` holds the box narrow to keep pinning how each fits.
- Found on the way, not fixed here (owner's call): `kronikol merge` draws a complex parameter value as the flattened
  string the mergeable JSON stores ("Ingredients: Flour: Plain Flour, ...; Toppings: 2 items"), where the run's own
  report draws it as an expandable value (`details.param-expand`, 3 cells in the parity run). The data is not in the
  file, so the fix is a format change: it belongs with REPORT_FORMAT_PLAN (the size reduction work) or a 4.x patch.
- 2026-10-18, S2: the run band (D3, D4) and the filter box on its own row (D5). The generator writes
  `section.run-band` (the ring, the summary zone, the CI zone) in place of the 4.x header row, its two tables and the
  pie's wrapper; the filter box follows with ids the bar will point at (`#filtering-box`, `#filter-rows`). The ring is
  drawn with classes the tokens colour, and its rate rounds down and stops at 99% unless every scenario passed (4.x
  read 213 of 214 as 100%); scenarios skipped after a failure are counted as skipped (4.x left them out of the
  summary and the ring). Both are bugs in 4.x too, recorded for the owner. `report-run-band-script.js` decides
  `.ci-below` on the zones' measured natural widths. Two preview defects found by the tests and not ported: the
  preview measured once at load, so text spacing applied afterwards (as a reader's extension applies it) left the CI
  zone wrapped under the summary without `.ci-below` (`ViewportSweepTests` at 1,280 to 1,400 px); the branch measures
  again when a zone's size changes, a frame later, outside the ResizeObserver callback. And the band's row wrapped,
  so on a phone a band with no CI zone put the summary under the ring (the parity run always has CI); the row no
  longer wraps. The filter box is a card like the band, with its title a label and its exports text buttons
  (stylesheet inventory 4, the container, title, header and export buttons only); not ported, the preview's
  `white-space: nowrap` on an export button, which 4.x removed because it pushed the buttons past a narrow box, and
  the 4.x blue border its hover kept. Left to their stages: the phone grid of the filter header and the shortened
  export labels (D13, S6, S12) and the readout (D14, S5). Measured with `checks/style-parity.js` (surface 2 added: the
  band, ring, titles, stats, CI zone, link and the filter box's chrome): the band matched the preview on its first
  measure, and the filter box's chrome went from 43 differences to 1 (the deliberate `nowrap`) at 1,400 px light and
  dark and at 700 px; at 400 px the rest are the phone grid left to S6 and S12. Surfaces 0 and 9 to 13 are unchanged.
  Tests: core
  `RunBandTests` (12 of 15 failed before), Playwright `RunBandLayoutTests` (one row while it fits, the CI zone under
  the summary and back, text spacing after load, the ring's and the filter card's tokens in both themes; each failed
  on the S1 stylesheet or the measure-once script), and the 4.x tests of the header row, its tables and its pie,
  rewritten for the band. The export row's -9 px margin lines the last label up with the card's content edge, so
  `ViewportSweepTests` and `ReportSectionWidthTests` check the labels against the content edge and the buttons
  against the box, where 4.x checked the bordered buttons against the content edge.
- 2026-10-18, S3: the report bar, its tabs and panels (D20), written by the generator. Each section that sat above
  the features as a panel of its own (the Scenario Timeline, the Component Diagram, History, Report diagnostics) is a
  tab of the bar (`div.report-tabs[role=tablist]`, `button.report-tab[role=tab]` with `aria-controls`, roving
  `tabindex`, a count on Features and Diagnostics), and `div.report-panels` holds one `div.report-panel[role=tabpanel]`
  per tab, all but the start tab's `hidden`. The sections' HTML is built where it was, so the diagrams keep their ids,
  and written after the features. The Features tab's tools are in `div.features-tools` under the tabs, hidden on the
  other tabs (S4 changes what is inside it). A page with nothing beside its features has no tabs, its features in a
  panel no tab names (the Specifications report keeps its one view, Q3). The Scenario Timeline and Component Diagram
  toggles go, with `toggle_timeline`, `toggle_component_diagram`, the timeline and component sections' inline
  `display:none` and the base sheet's `.timeline-toggle` rules (the violet theme's stay until S13).
  `report-tabs-script.js` switches the panels (`hidden`, `aria-selected`, `tabindex`, `.on-features`), draws a panel's
  diagrams when it shows (`_renderDiagramsInContainer`), moves between tabs with the arrow keys, Home and End, takes
  Next Failure, a link to `#scenario-` or `#sid-` and a scenario's address (on load and on `hashchange`) to the Features
  tab first, keeps the History and Diagnostics summaries from closing their sections, and sets `--kron-nav-h` to the
  bar's height while it sticks (0 on a phone, where the bar is static). It names no section, so a page without history
  or diagnostics names neither (the 4.6.0 guard); the two sections' in-panel margins are in their own sheets for the
  same reason. Q2 as built (section 7). Q4: the failure clusters and the background calls open the Features tab as
  cards drawn like a feature's, every colour on a token (they were fixed light colours), and a clustered scenario's
  feature is named through `.failure-cluster-feature` in place of an inline grey. The owner's R18.1 (section 3), given
  during the stage: `ShowHistorySection` and `ShowReportDiagnosticsSection` default to `true`, so a run that read
  history or recorded a diagnostic shows its tab, the scenarios' own history stays behind `ShowScenarioHistory`, and
  `kronikol ingest` follows, with `--no-diagnostics-section` and `--diagnostics-section` still taken. The bar's look is
  the preview's (stylesheet inventory 5): sticky, the tabs' accent underline, and on a desktop the bar running on into
  its panels on `--kron-panel`.
  The bar measured against the preview with `checks/style-parity.js` (surface 5: the bar, tabs, the selected tab's
  count, the tools strip, the panels): the same at 1,400, 700 and 400 px in Chrome and at 1,400 px in Firefox dark,
  but for the line under the tabs, which the preview draws on its `.nav-row` (S7) and the branch on the tabs until
  then; the counts take the sans with tabular figures, as the preview's type pass set them; on a phone the static bar
  keeps the card shadow the preview's contrast pass gave every card. Surfaces 0, 2 and 9 to 13 unchanged. Found by the
  tests and fixed on the way: the viewport sweep counted a button's text boxes as its lines, so a tab with a count read
  as wrapping; it now counts lines as runs of boxes that overlap, and shows every panel at once to measure them all.
  Tests: core `ReportTabsTests` (the bar's tabs and their order, the panels and which shows, no toggles or their
  functions, the clusters and background calls opening the Features panel with the feature named by class, the start
  tab from the options, no tabs on a page with nothing beside its features or on the Specifications report, the
  script; 9 of 10 failed before), the in-tab History section (`HistoryHtmlTests`) and the 5.0.0 defaults
  (`DefaultReportCleanTests`, `HistoryOutputsTests`, `ReportConfigurationOptionsDefaultsTests` and the ingest, merge and
  diagnostics tests); Playwright `ReportTabsTests` (one panel at a time and the tab order, the keys, the component
  diagram drawn when its tab shows, Next Failure, a history link and a scenario's address going to Features, the
  sections staying open, the sticky bar and its height, the phone's static bar, the tabs' and the cards' tokens in
  both themes). The 4.x tests that clicked the toggles open the tabs (`PlaywrightTestBase.OpenReportTabAsync`).
- 2026-10-19, S4: the Features tab's tools (D21, D22, D35) and the report bar's zones (D30), written by the generator
  in `div.features-tools-line`: the Full and Summary views as tabs (`div.view-switch[role=tablist]`, `button.view-btn`
  with `aria-controls`, roving `tabindex` and the titles of the 53-row table), the two fold groups, `span.tools-spacer`,
  the sort (`label.sort-group` > `select#features-sort`) and, on a page with tabs, the phone's Display settings gear
  (`button.tools-gear`, `aria-controls="display-settings"`, the bar's `.tools-open`). The features table is the
  Summary view: `div.features-panel` holds `div#features-table-view[role=tabpanel]` beside `#report-content`, one of
  them `hidden`, in place of the 4.x `details.features-summary-details` above the run band; the table gains the
  `tr.table-groups` row (Scenarios, then Steps and Duration when the run has them), its groups' first columns read
  Total, `.grp-start` rules each group off, and a feature's name is `button.feature-open`, so a row opens its feature
  from the keyboard too. `FeaturesSummaryOpen` opens the Summary view (Q2 as built). The fold groups are
  `span.fold-group` with `button.fold-btn.fold-expand` / `.fold-collapse` (`data-fold`, inline chevrons): the
  preview's `k-` prefix dropped, as no other page class has it; `aria-disabled` is seeded from `FeaturesExpanded` and
  `ScenariosExpanded` (both buttons with none to fold). The display settings are `div.toolbar-right#display-settings`
  with the zones `span.diagram-format` and `span.diagram-flags` after the payload radio; the scenario bars' zones,
  wording and chips are S8's. `report-features-tools-script.js` replaces `report-collapse-expand-all-function.js`
  (`toggle_expand_collapse` gone): the views (click, arrow keys, Home and End, the bar's `.features-table-mode`), the
  sort, a row opening its card in the Full view, the fold buttons (expanding the scenarios opens their rules and
  features; the dimmed state checked once a frame on `toggle`), the gear, and the Full view for Next Failure, a link
  to a scenario, a scenario's address and the arrow keys that move between scenarios. Item R17.28: the fold groups,
  their divider and the display settings hide in the Summary view.
  Preview defects not ported (structure inventory D21): the sort's columns were fixed numbers that held only with step
  columns, so without steps the three time sorts did nothing; each option now names its column (`data-col`), and an
  option for a column the table lacks is not written. "Feature A–Z" was the report's own order (the generator keeps
  the order it is given): it is now a sort by name, after "Report order". Rows were paired with cards by name, so two
  features with one name collided; they pair by position, taken as the page opens (a column click reorders rows
  later). Since round 17 the fold groups stayed in view over the table; they hide. On a phone round 17's fold groups
  lost the `order: 4` the buttons they replaced had, so at 400 px the line took three rows with the fold groups first;
  the branch keeps the desktop order on one row while it fits and, at 480 px and under, two rows (the views and the
  sort, then the fold groups and the gear). Two leftovers of 4.x rules in the preview's table card are not ported: a
  7 px band above the tinted heading row (the old wrapper's top margin) and an 18.75 px band under the last row (the
  old table's bottom margin). The column headings, which lost their hover to a later preview rule, keep one.
  Bugs found on the way and fixed: returning to the Features tab (S3) queued every diagram it held for drawing, those
  of closed scenarios too, since `_renderDiagramsInContainer` draws all it is given on a page that does not draw on
  demand; it takes `shownOnly`, which the tabs and the views pass. And the check for a shown diagram,
  `getClientRects()`, reads a box for content Chrome hides in a closed `<details>` (it uses `content-visibility`), so
  it now asks `checkVisibility()` first. In 4.x that check served only containers already shown. And the report's
  keys for moving between scenarios took the down and up arrows from every dropdown on the page (4.x too, in the
  changelog's Fixed): the keyboard handler leaves a `select` alone.
  Measured with `checks/style-parity.js` (surface 6, the tools line, and 9b, the table, added; a pick can name the
  branch's selector where its classes differ): 3 differences at 1,400 px in Chrome and Firefox, light and dark, all
  deliberate (the two bands above; the fold label's line height is 16 px, as the type pass gave the sort label, where
  the preview's later rule inherited 17.05), plus at 700 and 400 px the fold groups' and the sort's margins from the
  phone order. The table's figures take the sans with tabular figures, as the preview's type pass set them.
  Tests: core `FeaturesToolsTests` (the line's order, the views' markup, the table as the Summary view after the run
  band, `FeaturesSummaryOpen`, the column groups, the rows and cards in one order, the sort's options by column with
  and without durations, the seeded fold state, the gear and the zones, a page without the table; 10 of 11 failed
  before, the 11th pins the order the pairing relies on), `CollapseExpandAllReportTests` rewritten for the fold
  groups, and the pins of the old buttons and the bare `#report-content` tag in `ToggleDefaultsMarkupTests`,
  `ReportTabsTests`, `HistoryHtmlTests`, `ComponentDiagramReportTests` and `StylesheetRulesTests` (the violet theme's
  hover list; its two old selectors renamed until S13); Playwright `FeaturesToolsTests` (the views and what hides with
  the cards, the keys, the sort over cards and rows with two features of one name, the time sorts of a table without
  steps, a column sort cleared by the sort, a row opening its card by click and by Enter, expanding the scenarios
  opening their features, Next Failure from the Summary view, the phone's gear, the Features tab drawing only what
  shows, a dropdown's arrow keys; the last failed with the keyboard guard reverted, and the drawing test failed with
  `shownOnly` alone until `checkVisibility`). `PlaywrightTestBase` has `FoldAllAsync` and four helpers, which
  `ExpandFirstScenarioWithDiagram` and the 20 files that clicked the old buttons by label now use; the label-flip
  tests (`ReportToolbarTests`, `ToggleDefaultsTests`) check the dimmed state instead, the tests that opened the
  Features Summary open the Summary view (`DurationPaintTests` sorts the Duration group's Total), and
  `MobileResponsiveTests` opens the gear before it measures the display settings. Found by the suite: the sticky bar's
  test rounded the bar's height with `Math.Round`, which takes 128.5 to 128 where the page's `Math.round` gives 129; it
  rounds the page's way.
  Checks: the core suite passes 7,742 with 2 skipped; the full Playwright suite passes 1,104 with 28 skipped.
- 2026-10-19, S5 and S6: the filter pipeline (D14, Q5) and the filter rows (D6 to D13, D16, D19), done as one stage of
  work, since every change to a row runs through the pipeline. Pipeline: each scenario carries a flag for each filter
  (`tag`, the tags with Happy paths, in place of 4.x's `hp`, and `q`, the search's qualifiers) and shows while none
  hides it. `applyVisibility` writes the readout, `All N scenarios, F features` or `Filtered n of N scenarios, f of F
  features`, with a parameterized scenario counted once for each example (`data-runs`, written only past one), and
  raises `kron-filter-change` with `{ shown, total }` when the scenarios shown change. The link is version 2 (Q5):
  4.x's `deps`, `cats`, `depmode`, `catmode` and `hp=1` keep their meaning, and `tags`, `tagx`, `tagmode`, `depx`,
  `catx` and `hpx=1` say the rest, a chip for none by its reserved value. Clear All clears the qualifiers, every chip
  state and the modes, back to the report's seeded defaults. Rows (`report-filter-chips-script.js`, new, one
  delegated handler for clicks and right-clicks, which replaces `report-toggle-happy-paths-function.js`): the labels
  lose their colons and the controls sit in a wrapping group of their own (D6); Duration and Happy paths are on the
  Status line (D7); tags have a row (D9) and every chip row is tri-state (D9, D10), off, has (the row's 4.x active class) and has
  not (a new exclude class for each row, the compatibility contract's additive class; Alt+click and right-click go
  straight to it), with `aria-pressed` true, mixed or false, and a chip for none (`__untagged__`, `__none__`,
  `__uncategorized__`) where some scenario has none; each row has ANY / ALL (D11; the button says ANY or ALL and
  `data-mode` keeps OR or AND) and a Clear while one of its chips is set (D12; focus goes back to the row's first
  chip); Happy paths is Show, Only and Hide, the Happy Path tag chip under another face, and neither shows without a
  happy path (D8); the categories' All button gives way to the same chips (Q5). The exports say Filtered only where
  the box has room (D13). The search takes `dep:`, `duration:` and `is:happy` (D16), split out before it runs, so
  the deep search and the link see the rest; a qualifier it cannot read stays text and marks the field (`kron-q-bad`,
  `data-q-bad`), and the help names all three. The duration buttons name their threshold and share, and the warm-up
  where the run had one (D19). `(no interactions)`, the participant of a diagram with no call, was a dependency: it is
  not, and a 4.x link that chose it opens on No dependencies (the changelog's Fixed). D17, the deep search chip in the
  field, moves to S7 with D15: its 3em offset only goes wrong once More filters sits after the field.
  The stylesheet: `edits/edits-s5-a.py` (32 edits) replaces 4.x's chip, toggle, percentile and mode rules with the
  preview's final ones, makes the filter box a size container (`container-type: inline-size`, as the preview's is) and
  ports the rows' `@container` rules at 520, 430 and 300 px. Duration and Happy paths each take a line of their own
  until S7 measures when they fit beside Status (D18), which is what the preview does when they do not fit.
  Measured with `checks/style-parity.js`, surface fr (new, 23 picks): 0 differences at 1,400 px in Chrome and Firefox,
  light and dark. At 400 px, 14, each deliberate or left to a later stage: the phone grid's pills are round at every
  corner (the preview's cascade left the first and last of each group square on their inner side), tag chips are cut
  with an ellipsis as the others are, the Dependencies row no longer clips what overflows it (4.x's rule cut the
  chips' focus rings), the readout's phone line is S12's, and a label on a line of its own keeps a right margin the
  preview's cascade gave only some of them, which draws nothing. Every filter control is a 36 px touch target on a
  phone (4.x left the tag chips and Happy paths out). Surfaces 0 to 13 show the 10 known differences of S4 and no new
  one.
  Found on the way and fixed: `checks/cssedit.py` indented an edit's text by its rule's indentation on top of the
  text's own, so the rules S1 to S4 added sat 8 spaces too deep. `checks/css-reindent.py` (whitespace only, checked
  against the sheet with all whitespace taken out, and a no-op on 4.26.9's sheet) put 209 lines back, 1,868 bytes off
  every report, 796 of them S1 to S4's; the tool now dedents what it is given, and HEAD, reindented, with
  `edits-s5-a.py` gives the stylesheet byte for byte. The readout does not wrap, and in a filter box under 430 px it
  widened the actions past the box (`ReportSectionWidthTests` caught it): it wraps there, as the preview's rule has it.
  Tests: core `FilterRowsTests` (new, 10 facts: the rows' markup, the chips with their counts and the chips for none,
  the modes, the Clear, the readout and `data-runs`, the exports, the search's placeholder and help, the duration
  titles; 9 failed before), `ExtractDependenciesTests` (the marker participant is no dependency), `VerdictFencesTests`
  (the helper's and the search function's pins are 5.0.0's text, with 4.5.0's verdict lines), and the pins of the old
  markup in `UrlHashFilterTests`, `ToggleDefaultsMarkupTests`, `WarmUpHtmlTests` and `StylesheetRulesTests`;
  Playwright `FilterPipelineTests` (new, 24: the tag cycle, Alt+click and right-click, ANY and ALL, the chips for
  none, the dependency and category tri-states, Happy paths' three ways, a row's Clear, the readout and its events, a
  link there and back and a 4.x link, 12 qualifier searches, a qualifier it cannot read, Clear All, the CSV export of
  a filtered page). The tests that clicked Happy Paths Only or read OR and AND (`ScenarioInteractionTests`,
  `UrlHashFilterHighlightTests`, `ToolbarStylingTests`, `FilterModeDefaultsTests`, `ShowcaseReportTests`,
  `WikiGifTests`) use the new controls, a chosen filter is checked against `--kron-selected` as the page resolves it
  rather than 4.x's solid blue, and `ReportSectionWidthTests` holds the filter box at 100 px, under the width of
  Export HTML, now that 140 px fits it whole.
  Checks: the core suite passes 7,753 with 2 skipped; the full Playwright suite passes 1,128 with 28 skipped.
- 2026-10-19, S7: More filters (D15), the report bar carrying the filters (D24, item R17.11) and pinned (D25), the
  measured layout (D18), and the deep search chip in the field (D17, moved here from S6). The generator writes the box
  folded (`rows-folded`) with More filters at the end of the search line, its title naming the rows the box has; the
  search field in `span.search-field`; the bar's mark, one `div.nav-mark` for both pinned and floating (the preview put
  a mark and a sentinel at the same place); the bar's top row, `div.nav-row`, holding the tabs and `div.nav-filters`
  (the readout's long and short copies, `nav-clear` and `nav-filters-toggle`); the box's close for the bar, hidden; and
  the empty state, first in the Features panel. A page without tabs keeps More filters and the empty state and has no
  bar group. `report-filter-bar-script.js` (new) counts the filters on in the folded rows (each status, each chip set
  either way, one for the duration) and keeps More filters' title and label; copies the readout into the bar on
  `kron-filter-change`, shortens it, then leaves it out, before the tabs would scroll (`nav-compact`, `nav-minimal`);
  wires the bar's Clear All and the empty state's, which puts focus in the search; keeps the scroll as the preview did
  (`settle`, `reading` and `keep`, scroll anchoring off while the box moves); takes the box into the bar and back, caps
  it at 60% of the window, and closes it on Esc, its close, a phone's width or the bar coming to rest; marks the bar
  floating (`k-floating`, the pin line included) and pinned (`is-pinned`); and sets `pair-fits` and `trio-fits` from
  widths measured on hidden clones, again when the Status row or the box changes size or fonts land. `toggle_search_help`
  marks the box `help-open` itself (no observer), and `report-search-index.js` puts the chip in the field. Left out: the
  preview's `window.__kronikolNavFilters`, which only its harness read; `.zone-adjacent` goes with S8's zones.
  Preview defects not ported: `keep` corrected the scroll once, and when the bar pinned or unpinned in between, closing
  the box left the bar at rest with the box in view instead of what was being read; it corrects until it holds
  (`FilterBarTests` compares what is just below the bar before opening and after closing, and fails with one pass). The
  box in the bar draws its rule on its bottom edge, between it and the tabs, since round 17 put it at the top of the bar
  (round 8's rule stayed on its top edge). More filters answers a click from the moment the script loads, through one
  delegated handler, where a click before the preview's delayed start did nothing.
  Measured with `checks/style-parity.js`, surface nb (new: More filters, the search line, the bar's row, group and
  readout) and fr: 0 differences at 1,400 px in Chrome and Firefox, light and dark. At 400 px the search line keeps
  4.x's grey sticky background and padding, which the preview took out, and the readout stays in the box rather than on
  the Filters line: both are S12's. Surfaces 0 to 13: the known differences of S4 and none new.
  Tests: core `FilterBarTests` (new, 6 facts: the folded rows and More filters with its title, the title for a box
  with only Status, the bar's mark, row and group, the box's close, the empty state, and a page without tabs; all 6
  failed before), and the pins of the bar's and the box's opening markup in `ReportTabsTests`, `RunBandTests` and
  `FilterRowsTests`; `VerdictFencesTests` holds the search function's and the search index's 5.0.0 text. Playwright
  `FilterBarTests` (new, 12: folding and opening, the count from a link and from the rows, the help while folded, a
  phone, the bar's copy and Clear All, Filters only while the bar floats and the box in and out with Esc and the
  close, the box going back as the bar comes to rest, the empty state and its focus, the pinned bar, the measured pair
  and trio and their undoing in a narrow box, the bar's two narrow forms, the chip in the field; the ride test reads
  what is below the bar once the page has stopped moving, since opening the features brings a scrollbar that the
  measured layouts follow a frame or two later). The 31 tests in 10 files that used a row's control on a desktop open
  the rows first with `PlaywrightTestBase.OpenFilterRowsAsync` (new), and `ReportToolbarTests` finds the box's Clear
  All by its class, now that the bar and the empty state have one too.
  Checks: the core suite passes 7,759 with 2 skipped; the full Playwright suite passes 1,140 with 28 skipped.
- 2026-10-19, S8: the scenario toolbars split (D29 to D34, D37), the scenario's settings (D23, sequence-only toolbars
  included, items R17.23 and R17.26), the controls' loading state and bold widths (item 25), and the line count's
  heading (R17.27); the format dropdown's heading (R17.3), listed under S9, came with the selects' wording. The
  generator writes a diagram section's head once, for a scenario and for a parameterized group
  (`AppendDiagramSectionHead`): the title; the views' tabs on `div.diagram-toggle-tabs[role=tablist]`, each `role=tab`
  with `aria-selected` and a roving `tabindex`; and, where the section has sequence diagrams and the page draws them,
  `div.scenario-settings` holding the options bar (`div.diagram-toggle.diagram-options-bar`, starting
  `span.scenario-settings-label` "This scenario:", then the three zones) and `button.scenario-settings-gear` (the
  preview's cog path, written out), with the phone's gear in the title (`button.gear-in-summary`, U+2699). A section
  opening on another view carries `diagrams-not-seq`. The Payloads radio, its "N lines" options under "Payload
  length", the bare toggle words with `aria-pressed` and `span.btn-label[data-label]` come from the shared builders,
  so the report's bar has them too; the format and width dropdowns are "Payload format" (with its heading, the owner's
  pick over Chromium's 15 px) and "Payload width". `report-scenario-settings-script.js` (new) opens and closes the
  settings from either gear (one state, `is-open`, both gears' `aria-expanded`; the title's gear does not fold the
  section) and sets `.zone-adjacent` on an open bar's toggles where they sit beside the dropdowns (D18's last part).
  `toggle-script.js` keeps `aria-selected`, the tabindex and `diagrams-not-seq` and moves along the tabs with the
  arrow keys, Home and End; `syncToggleBtn` writes `aria-pressed` and leaves the label alone; the note's width button
  says "box"; `report-init-script.js` no longer makes the phone's "Diagram Settings" button. Stylesheet: the tabs bar
  and the section's card in `stylesheets.css`; the payload controls, the zones, the loading state, the bold widths,
  the settings, the gears and the phone's sheet (every bar's zones one under another, headed Format and Toggle) in
  `collapsible-notes-styles.css`, which only a report drawn in the page carries; the span-count warning a pill.
  Deviations from the preview: the options bar carries no `not-seq` class of its own (the section's class hides the
  settings, gear and all); a section with no sequence diagrams has no settings (the preview wrote its bar and always
  hid it); the span-count warning stays on the tabs bar (the preview's split left it in the options bar, hidden with
  it), and a section with one view and no tabs carries it on its title; the line count and the width read in capitals
  by `text-transform`, their text "3 lines", "Wrap" and "Wide" as a screen reader should say them (the preview wrote
  "3 LINES", "WRAP" and "WIDE"); the width dropdown takes the 34 px phone touch height the other controls take (the
  preview's rule missed it); Firefox keeps R17.8's dot offset on a phone's report bar too, which the preview's later
  phone rule undid there only; the settings share one open state at every width (the preview's phone button and its
  gear kept two); the tabs move with the arrow keys and keep a roving tabindex, as the report's tabs and the Full and
  Summary views do (the preview only gave them roles); a phone's view tabs share their row but never grow narrower
  than their longest word (the preview's `min-width: 0` let them, and at 320 and 340 px "Sequence Diagrams" broke
  inside its words over four lines); and the title's gear calls no `preventDefault`, as the preview's did: a button in
  a summary is its click's own target, so the section stays open without it, by click, Enter and Space, in Chrome and
  Firefox (measured; a mutation removing the call survived, which is how it was found).
  Bugs found on the way and fixed: once the arrow keys had picked a scenario, Enter folded it wherever the keyboard's
  focus was and the focused button or heading did nothing (4.x too; the handler now acts only while nothing has the
  focus); a scenario with one view of its flow and no sequence diagram lost its span warning (4.x too); and S1's
  restyle of the steps had dropped `user-select: none` from a step's status icon, which a bare-substring test kept
  passing because other rules on the page carried both of its strings (it now reads the rule).
  Measured with `checks/style-parity.js`, surfaces 7a (the report bar's controls), 14 (a scenario's section, tabs and
  settings) and 14o (the settings opened), with pseudo-elements, transitions off (a page busy drawing every diagram
  makes no frames, and a transition read after a click had not started) and Firefox at a phone width through a frame
  of that width (this Firefox build takes no viewport from the driver): at 1,400 px in Chrome and Firefox, light and
  dark, and at 400 px, only the differences named above remain, with the report bar's Events and Setup toggles (D38, a
  later stage) and the diagrams' own heights (S10).
  Tests: core `ScenarioToolbarTests` (new, 9 facts: the tabs bar then the settings behind the gear, a section on
  another view, a sequence-only section, the Payloads wording and bold-width spans, a toggle's bare word with
  `aria-pressed`, the format and width wording, a section with no sequence diagrams, the script only where settings
  exist, a report drawn when written; 6 of the first 7 failed before), `DiagramToggleReportTests` (the one-view
  warning, which failed before, and the tabs bar's warning), and the toolbar pins moved to the new markup in
  `ToggleDefaultsMarkupTests`, `ParameterizedGroupRenderTests`, `ReportToggleTests`, `DatabaseToggleReportTests`,
  `TrackThatIntegrationTests`, `NoteAppearanceScriptTests` and `StylesheetRulesTests`; `StepRenderingReportTests`
  reads the step icon's rule. Playwright `ScenarioSettingsTests` (new, 9: the gear opening and closing in place, the
  open bar under the report's bar, stepping aside on another view, the arrow keys, the phone's title gear leaving the
  section open, a toggle's word and width, a payload button's width, the line count only while Truncated, Enter
  pressing the focused button); `PlaywrightTestBase.OpenScenarioSettingsAsync` (new) opens a scenario's settings, the
  way a reader would at the page's width, before the 67 places in 24 files that use its controls;
  `MobileResponsiveTests` and `ViewportSweepTests` use the title's gear (the sweep opens every scenario's settings at
  every width, and lets a phone's view tab wrap at a space but not inside a word); `DiagramToggleLayoutTests` lays out
  the tabs bar, and checks that the settings open on a row of their own; the label assertions read the word and
  `aria-pressed`; `ToolbarStylingTests` read the C look on the default theme (the violet theme's rows keep its 4.x
  colours over the new base until S13); `NoteAppearanceTests` checks the 6 px between the format and width dropdowns;
  `WarmUpMarkTests` finds the diagram's picture by its container, as the gear is an svg too. `checks/mutate-s8.py`
  undoes six of these behaviours one at a time and runs the test meant to catch each: all six fail (Enter folding the
  picked scenario again, a toggle rewriting its label, the tabs' arrow keys doing nothing, the title's gear opening
  nothing, the open settings not sticking under the bar, a label losing its bold width).
  Checks: the core suite passes 7,770 with 2 skipped; the full Playwright suite passes 1,149 with 28 skipped, and one
  test timed out under load (ArrowLinkOpensPopupTests, whose 300 s wait for every diagram on its page to draw ran out;
  the class passes 8 of 8 run alone).
- 2026-10-19, S8 follow-up: hiding assertion notes or step bars took the line break on both sides of each, so wherever
  a diagram's lines end in LF the statement before it ran on into the one after it, and a diagram of nothing but an
  assertion note reached the engine, which failed on it, in place of the "Nothing to draw" message (4.x too; a report
  written on Windows keeps a lone CR that PlantUML reads as a line break, which is why the parity run showed nothing).
  Each marker now goes with its own lines and the one break after them. The message named the labels S8 retired
  ("Assertions Shown / Steps Shown"); it names Assertions and Steps.
  Tests: Playwright `FilteredMarkerTests` (new, 3, all failing before), and core `StepBarPlantUmlTests` reads the
  strip regex from the script, where it kept a copy whose Contains check could not see the lines run together.
  Checks: the core suite passes 7,770 with 2 skipped; the full Playwright suite passes 1,153 with 28 skipped.
- 2026-10-19, S9a: the Events and Setup toggles (D38, P4 to P8, P11) on one model for every diagram filter (F2, F5,
  F11). In the page, `stripEventCalls` is the databases filter with `queue` for its participants' keyword
  (`stripDatabaseCalls` became `stripParticipantCalls(source, kinds)`, as P4's port note asks, in place of the
  preview's clone of its text) and `stripSetup` is the preview's; `applyFilters` applies the five in one order, the
  setup first, and every reader uses it (`composeNoteSource`, `_preProcessSource`, the note-index replay, and
  `buildFilterQueue`, one builder in place of three that differed only in their flag). One model (F5): a scenario's
  toggle records its choice on the scenario, the report's toggle clears every scenario's, and `scenarioChoice` gives a
  diagram drawn later its scenario's choice, else the report's; `filterShown` resolves a missing flag that way for all
  five (F2). The generator writes the two toggles after Databases in the report's bar and every scenario's settings,
  gated report-wide like the others (a `queue` participant; a Setup frame or a When step bar, `SetupMarker`), not only
  beside a Databases toggle (F11), with `ReportToggleDefaults.EventsShown` and `SetupShown` (new, default true) and
  their script tokens.
  Measured on the way: a diagram is given its source only when it is drawn, and in a page that draws its diagrams
  every diagram of an open scenario is drawn at once (a parameterized scenario's hidden examples too), so the 4.x
  model's gap (a diagram drawn after a scenario's toggle took the report's state) was open only while a source was
  still being decompressed. The tests reach it by drawing a diagram into the scenario after its toggle, the way a
  later diagram arrives; no 4.x bug is claimed.
  Measured with `checks/style-parity.js` at 1,400 px in Chrome and Firefox: the toggle zones of the report's bar and
  of a scenario's settings now match the preview's, D38 having been their last difference.
  Tests: core `EventsAndSetupToggleReportTests` (new, 11); `DatabaseToggleReportTests`'s two script pins name
  `buildFilterQueue`; `ToggleDefaultsMarkupTests`'s token list carries the two new tokens. Playwright
  `EventsAndSetupToggleTests` (new, 4) and `FilterToggleModelTests` (new, 6). `checks/mutate-s9a.py` undoes five
  behaviours one at a time, and each fails its test; the setup filter running after the steps filter survived a first
  run, as no test hid the step bars and the setup together, and one now does.
  Checks: the core suite passes 7,781 with 2 skipped; the full Playwright suite passes 1,163 with 28 skipped.
- 2026-10-19, the owner's defaults for 5.0.0 (asked during S9a: "for v5 I want all the report level diagram toggles
  except Headers to be on by default, and the default should be Truncate 10 Lines"):
  `ResolvedToggleDefaults.AssertionsShown` is true (false from 4.0.0) and `TruncateLines` is `Lines10` (`Lines40`
  through 4.x); Steps, Databases, Events and Setup already started on, and Headers stays off. A change of defaults,
  listed in the changelog's breaking changes, with the two settings that open a report as 4.x did. The built-in pins
  move; a fact that seeded 10 lines, now the default, seeds 20; `ReportTestHelper.ClassicStart`, the start the older
  fixtures were written for, pins assertions hidden and 40 lines as well as 3.x's headers and JSON; the facts that
  read a long note whole write their reports with 40 lines, and those that relied on assertions starting hidden write
  them hidden.
  Tests: Playwright `ToggleDefaultsTests` gains a fact that a report with nothing configured draws its assertion notes
  and truncates a long payload at 10 lines (failing before).
  Checks: the core suite passes 7,781 with 2 skipped; the full Playwright suite passed 1,160 with 28 skipped and
  failed 4, all facts written on the old start, fixed and passing 19 of 19 in their classes:
  ComponentDiagramDatabaseToggleTests's Assertions click now hides them (after checking the notes are there),
  FilterStateDefaultTests's assertions row reads the notes' stereotype, as the fixture's style block names
  .assertionNote and the bare word made the row unable to fail, and CapturedTextEscapeTests's fixture, whose facts
  read notes longer than 10 lines, truncates at 40.
- 2026-10-19, S9b: a collapsed payload's one-line preview leaves out a JSON body's braces (P1): the opening brace
  goes, and the closing one too when the whole body fits; a longer body is cut after its brace; "{}" and an array are
  unchanged. P9 (the exact note map) is not ported: F6 did not reproduce at v5. With the Databases or the Setup filter
  off, whether clicked or configured before the report opens, a note's buttons act on their own note, the case F6
  describes ("1 row" taken out, "1 row" kept after it); `ExactNoteMapTests`, written to reproduce it, pass before any
  change and stay as regression tests. 4.26.8's note index map covers the buttons; the text match in
  `makeNotesCollapsible` is left as it is until something reproduces through it.
  Tests: Playwright `NotePreviewBraceTests` (new, 7, failing before) and `ExactNoteMapTests` (new, 2);
  `NoteHeaderFormsTests` reads the preview without its braces.
  Checks: the core suite passes 7,781 with 2 skipped; the full Playwright suite passes 1,173 with 28 skipped.
- 2026-10-19, S9c: the note buttons (P15: B1 to B8, items R17.9, R17.13 to R17.15 and 24) and their tooltips (D42),
  which finishes S9 (the format dropdown's heading, R17.3, came with S8). `createNoteButtons` is the preview's round
  17 function, re-derived on the product's (`scripts-s9-d.py`): the triangles are drawn (`drawnTriangle`, 2 px clear
  of their button's top and bottom edges, the top-right one 3 px); every top-right button is made at the first place
  from the note's right edge and moved by `packTopRight`, which skips a hidden one and carries a tall note's scroll
  offset, so the ▲ follows the visible part of a tall note too; a truncated note's bottom holds `collapse-bottom` and
  `expand-bottom` side by side, each 24 px with 3 px between; a collapsed note that already shows all of itself
  (`collapsedIsWhole`, P10) has no + and no bottom ▼; and making a note shorter from its lower half brings its new
  bottom back to the click (`keepPlace`). The buttons are inked from `--dg-icon`, `--dg-icon-edge` and `--dg-icon-ink`
  (`collapsible-notes-styles.css`).
- Not ported, as preview defects: `keepPlace` watching the fragment it was given, which a redraw replaces, so a
  diagram drawn in fragments never scrolled (F4: it watches the diagram that owns the fragments, which carries the
  rendering flag); the unused slot parameters (F8); the copied "Fragment note indexing" comment (F13); and the
  triangles without tooltips (rule 7). D42's titles are set by name when a button is made (`giveTitle`,
  `scripts-s9-e.py`), not by glyph on first hover as in the preview: − and the bottom collapse "Fold this box to one
  line", the two ▲ "Cut this box back to the line limit", the bottom ▼ "Show more of this box", and the + "Open this
  box in full", or "Open this box to the line limit" for a note longer than the limit, since a click on that + opens
  it to the limit (round 9's wording described a short note). `kTriangle` and `_kdy` are renamed `drawnTriangle` and
  `_followDy`.
- Found on the way and fixed: `CssRules` split a selector list at every comma, inside `:is()` too, so
  `Every_selector_in_the_built_in_sheets_is_well_formed` read the four new rules as broken; it now splits at the
  list's own commas (the preview's diagram look, S10, has 24 more such selectors). F16: the two stale copies under
  `tests/TestTrackingDiagrams.Tests.EndToEnd/`, with no project, unchanged since 2026-06-07 and still finding the
  buttons by glyph, are removed.
  Tests: Playwright `NoteButtonLayoutTests` (new, 9, each failing before: the drawn triangles and their clearance, a
  truncated note's pair, no gap for a hidden button, nothing to open on a whole collapsed note, the cluster following
  a tall note, the new bottom back at the click in a whole and a fragmented diagram, and the titles of a long note's
  buttons in its three states and of a short note's +). The 24 locator lines in 7 files that found ▲ and ▼ by their
  glyph find the buttons by name (`data-note-btn`, B6, `tests-s9c-a.py`), and `DiagramNotePlaywrightBase`'s wait polls
  every 200 ms (F15). `DiagramNoteMultiDiagramTests` no longer expects + after collapsing a split note that shows all
  of itself, and `ExactNoteMapTests` waits for the note's state after its − click, not for its icons (B1). Core:
  `CssRulesTests` (new, failing before); seven `DiagramContextMenuTests` facts and one `NoteAppearanceScriptTests`
  fact move their pins to the drawn triangles, the packing, B1's condition and `keepPlace` (`tests-s9c-b.py`).
  Mutations: 10 of 10 killed (`checks/mutate-s9c.py`).
  Checks: the core suite passes 7,782 with 2 skipped; the full Playwright suite passes 1,182 with 28 skipped.
- 2026-10-19, S10a: the emitter's half of the diagram look (P17 to P22, P35 to P37), on Q6's recommendation: what the
  C# source can set is set there, for every rendering. A sequence diagram's source starts after its pragma with the
  look (`DiagramLook`): no shadows, messages padded by 4 and centred, participants with rounded corners, padding and
  thin edges, the other shapes thin edges, thin lifelines and note edges; and the system's own font (`skinparam
  defaultFontName system-ui`) where a browser engine draws it: `GetPlantUmlImageTagsPerTestId` takes `systemUiFont`,
  which the fetcher sets for BrowserJs and NodeJs rendering, since the JVM (Server, Local) knows no `system-ui`.
  Component diagrams and their diffs drawn without C4, which is how the JS engine draws them, take the font too. A
  diagram with a user's `PlantUmlTheme` leaves the look out and keeps the theme's (not in the preview, which had no
  theme; a question for the owner). Services are participants, drawn as boxes, and so is a caller after the first that
  has no category (P20). Databases are deep cyan `#0E7C86` in the palette (P35, R17.22), and a component database box
  `#0B6C75` edged `#09616A`. The cache and queue boxes are `#B35900` and `#8E44AD`, so their white names read at AA,
  4.83:1 and 5.87:1 where they read 2.19:1 and 4.67:1 (the S10 row's "cache box's contrast", P36.20, P37). A step
  bar's table or doc string ends at its last row (P22, R17.6). The donut was done in S2, whose ring takes the tokens
  by class; the diagram's background needs nothing in C#, its light value being the engine's own white (the dark one
  is the page's, S10b).
- Deviation from the preview: the look is written one rule to a line, each line opening `sequenceDiagram {`. The
  preview added its look at render time, so the source's line readers never saw it. Written into the source one
  property to a line, `  note {` opened a note for the query tool's payload reader (`PayloadReader.IsNoteOpener` takes
  "note" and a space), `  participant {` was a declaration to the report's Setup filter (`stripSetup`'s `DECL` takes
  "participant" and any word), and the suite's note and partition balance checks counted the style's note line.
  Measured on the pinned engine (1.2026.8, `plantuml-render.js --batch`, `checks/look-one-line-probe.py`): the
  one-line form draws the same SVG byte for byte as the preview's form, 4,730 bytes against 4,578 with no look.
  Tests: core `DiagramLookTests` (new, 14 facts and cases: the font where a browser engine draws and not for the JVM,
  the look and its place after the pragma, no look line read as a note or a declaration, a user theme's own look,
  services as boxes, the database colour, the component diagram's and the diff's font, every component box's white
  name at AA, measured as WCAG contrast), and `PlantUmlBrowserDiagramFetcherTests` (the font by rendering; the "not
  Mermaid" fact reads Mermaid's header line, now that the style names its diagram type). The 14 pins of `entity`
  declarations in `PlantUmlCreatorTests` and the names of nine of its facts say participant; six pins of the database
  colour move to `#0E7C86`; the step bar's six pins in `StepBarPlantUmlTests`, four in `CapturedTextEscapeTests`
  (which found a doc string's last line by the blank line under it, and now read it at the bar's end), the ingest
  pipeline's and Playwright `StepTableBarTests` (no blank line painted below the last row) end a bar at its last row.
  Mutations: 12 of 12 killed (`checks/mutate-s10a.py`). The edit scripts are in `edits/s10a/`; the change was made in
  a second worktree while S9c's suites ran, and applied to the branch as one patch.
  Checks: the classes S10a touches pass on the branch (core 1,328: PlantUml, ComponentDiagram, PlantUmlBrowser, the
  ingest and payload readers, the Setup and dependency filters; Playwright `StepTableBarTests` 3); the full suites run
  at the end of S10, as the machine was another session's for its own full runs.
- 2026-10-19, S10b-1: the render shim's half of a source's look (P21, P22 for older sources; the preview's bands
  lever). The shim draws every step bar as a rectangle (`rnote across`) with its label in bold, which the role pass
  gives the band's colours, and drops the blank line under a step bar's table or doc string that a report before 5.0.0
  wrote. It rewrites what the engine draws and the cache key, in render and in prefetch, and never the source the page
  keeps, which its filters, menus and notes read. As the inventory recommended, the rectangle stays a rewrite in the
  page: emitted from C#, every reader of step bars (the strips, `stripSetup`'s rule, the statement limits, the ingest
  records) would need two shapes, and older sources would still hold the hexagon.
  Tests: Playwright `DiagramLookShimTests` (new, 3: the bars drawn as rectangles with a bold label and the older bar's
  blank line not drawn, each failing before; the page keeping the report's own source). Mutations: 3 of 3 killed
  (`checks/mutate-s10b-a.py`).
  Checks: Playwright `DiagramLookShimTests`, `StepTableBarTests`, `FilteredMarkerTests` and
  `EventsAndSetupToggleTests` pass (13); the full suites run at the end of S10.
- 2026-10-19, S10b-2: the role pass (P24 to P30), the preview's `c-diagram-look.js` section 2 re-derived at the end of
  `plantuml-browser-render-script.js`. Once the page has drawn a sequence diagram, each element is found by its
  structure and given a role class, which `inline-svg-styles.css` colours from the theme's tokens (P31: the preview's
  role rules and round 17's step table header and rules): lifelines and participants; step bars, which the shim draws
  as rectangles, as light bands with dark ink; a step bar's table on its own ground with its header row shaded, the
  bar's contents lifted 1.5 units; every strip (a step bar, an assertion, the Test bar, a Row marker) stretched to the
  drawing's full width with what is drawn in it centred again; notes redrawn as rounded cards of the same bounds, an
  event note as an event card, the fold kept, hidden, for the note script that finds notes by it. The shim marks a
  sequence diagram's container (`data-k-sequence`, from its source's pragma) and records its note kinds; a
  MutationObserver tags every picture as it arrives, and an IntersectionObserver one drawn hidden when it comes into
  view; `data-k-roles` holds the counts and the time taken. Measuring, deciding and writing are three phases, so the
  drawing is laid out once.
- Not ported: the levers (the owner took every one but the badges), the number badges (P29, off since round 8), and
  the note kinds read from one stereotype (F3): a note widened by its width button carries
  `<<kronNoteWide>><<eventNote>>`, which the preview skipped, so the kinds no longer matched the notes drawn and every
  event card in the diagram lost its colour. The port reads every stereotype and takes the one ending in Note.
  Left out until S10c: exports. A copied, saved or opened SVG or PNG carries no page CSS (F9), so the roles' colours
  are lost there, and the two rects the pass adds behind a step table, with no fill of their own, export black; S10c
  carries the look into exports.
  Tests: Playwright `DiagramRolesTests` (new, 9, each failing before: lifelines and participants in their colours, the
  light band and its dark ink, the step table's ground and shaded header, a strip the drawing's full width with its
  label centred, notes as cards and an event card with the fold hidden, a widened event note keeping its colour, a
  card keeping its note buttons, the dark theme's colours, a diagram drawn again tagged again). Mutations: 9 of 9
  killed (`checks/mutate-s10b-b.py`). The first run's two survivors found the strip fact comparing the band with a
  note in the middle of the drawing, which an unstretched band passed too (it now takes the rightmost card), and a
  mutation too weak to stop a redrawn picture's tagging (it now tags only a container's first picture).
  Checks: `DiagramRolesTests` pass on this source, and the 435 Playwright facts whose class or name says note,
  diagram, step, marker, toggle, context menu, split, fragment, render or internal flow passed on it before a cleanup
  that changes no behaviour (a redundant condition dropped, two comments reworded). The role pass's cost against
  `BrowserRenderWorkerTests`' main-thread budgets, which the batch's filter did not reach, and the full suites are
  measured at the end of S10.
- 2026-10-19, S10b-3: the colours the engine paints in a sequence diagram, mapped to the theme's tokens (P36: the
  preview's `c-round5.css` 90-110, keyed on the product's own literals since S10a), at the end of
  `inline-svg-styles.css` (`edits/scripts-s10b-d.py`): the drawing's ground, its ink, its lines and arrowheads, the
  assertion strips, a bar that is not a step, and the three dependency colours a call is drawn in (HTTP `#438DD5`, a
  database `#0E7C86`, a message queue `#9B59B6`). The light theme takes Option C's softer ink and lines; the
  dependency colours keep the palette's values there and are lightened on the dark ground. The participants and notes
  take their colours from their roles (S10b-2).
- Moved to S11: the preview's rules for the internal-flow activity diagram (`#f0f4ff`, `#666`, `#ccc` and the soft ink
  of `#666666` and `#686868` text) and for the component diagram (`#08427B`, the database box's `#09616A` edge), which
  the popup and the Components tab draw: they are written there with tests on those pages, since the popup's test page
  carries none of the report's theme styles. Not ported: the preview's fallbacks for the participant and note colours
  (`#E2E2F0`, `#FEFFDD`, `#CFECF7`), which the role pass covers.
  Bugs found on the way: none in the product. The tests' first draft looked for a line's colour in its style; the
  pinned engine writes it as an attribute (`stroke="#181818"`), which the rules match as well as the style form, and a
  lifeline is drawn in the arrows' colour, so the arrow is the level line.
  Tests: Playwright `DiagramThemeColoursTests` (new, 3 theories, 10 cases in the light and the dark theme: the ground,
  ink, lines and arrowheads; the assertion strips and the Test bar; each dependency call and its arrowhead). 7 of the
  10 failed before; the other 3 are the light theme's dependency colours, which are the palette's own. Mutations: 13
  of 13 killed (`checks/mutate-s10b-c.py`, one rule out at a time).
  Checks: `DiagramThemeColoursTests` and `DiagramRolesTests` pass (19); the 445 Playwright facts whose class or name
  says note, diagram, step, marker, toggle, context menu, split, fragment, render or internal flow pass; and
  `BrowserRenderWorkerTests` passes (19), which S10b-2's role pass also had to: on the large fixture the worst
  main-thread task took 122 ms against its budget of 500 ms (145 and 167 ms in today's runs before it).
- 2026-10-19, S10c, which finishes S10's page work: a diagram stands in the middle of its box (the owner's round 17
  item 19, `c-round17.css` 68-74), a request's link takes the report's accent (item 5, `c-round17.css` 79), and a
  copied, saved or opened diagram carries the look (Q6, F9), in `edits/scripts-s10c-a.py`. A diagram narrower than its
  box is centred with auto margins, and the box holds it 1em in from either side, where it held it 1em from the left
  and 0 from the right; one as wide as its box still fills it. A link painted in the engine's #0000FF and underlined
  (on hover, or always where links are always shown) is drawn in `--kron-accent`. `context-menu-script.js` writes each
  drawn element's computed paint into the copy where the stylesheet changed it, and its visibility where hidden (the
  fold), and the copy without transparency takes the drawing's ground: Copy as SVG, Save and Open as SVG, and both PNG
  copies. The rect the role pass draws behind a step table, with no fill of its own, was drawn black in every copy
  (F9, left open by S10b-2).
- Deviations: a diagram drawn in fragments is centred too (`.puml-fragment > svg`), which the preview's rule did not
  reach, so a long diagram stood at the left while a short one stood in the middle. The link rule is kept to links
  painted in the engine's own blue, so a theme's link colour stays its own, as 4.x keeps it (DIAGRAM_COLOURS_PLAN
  12.5). A copy takes the light theme's look whichever theme the page shows, as every copy did before 5.0.0: in the
  dark theme a transparent copy would paste light ink onto the white documents it nearly always goes into. The theme
  is switched for the copy and put back in the same task, so the page paints nothing in between (a question for the
  owner, who may want the shown theme).
  Tests: Playwright `DiagramCentringTests` (new, 2: a diagram drawn whole and one drawn in fragments, each within 1 px
  of its box's middle), `DiagramLinkHoverTests` (new, a theory in both themes) and `DiagramExportLookTests` (new, 2
  theories, 6 cases: an SVG copy in both themes carries the light theme's step table ground, ink and card colours,
  keeps the fold hidden and leaves the page's theme as it was; each PNG copy in both themes paints the table's ground
  white, which was black). All 10 failed before. The export tests' first draft waited for the copy with an async
  predicate in WaitForFunctionAsync, and its PNG cases still read the clipboard before the image was written: they
  poll inside their own evaluation. Mutations: 10 of 10 killed (`checks/mutate-s10c-a.py`).
  Checks: the three classes and the context menu tests pass (22); the full suites run at the end of S10.
- 2026-10-19, S10b-3 follow-up: S10b-3's colour rules reached three things they should not have
  (`edits/scripts-s10b-e.py`, `edits/scripts-s10b-f.py`). (1) The report's component diagram, drawn by the page in a
  `.plantuml-browser` box too, fills a system box `#438DD5` with a white name, and the HTTP colour's fill rule gave it
  the dark theme's lighter blue (`#6AA5E4`), which left the name at about 2.5:1, below AA: a sequence diagram fills
  only a call's arrowhead in its colour (its participants take their roles), so the three fill rules keep to polygons.
  (2) A scenario's whole-test flow is an activity diagram (`.iflow-diagram`) with black text on light boxes
  (`#f0f4ff`), which the ink rule turned light in the dark theme, unreadable on those boxes: the whole block leaves
  `.iflow-diagram` alone. The scope is written `:where(:not(.iflow-diagram))`, which adds nothing to the rules'
  specificity: as `:not()` it tied the roles' doubled classes, and the line rule, coming later, would have taken the
  lifelines', participants' and cards' edges. (3) ComponentDiagram.html carries `inline-svg-styles.css` but not the
  token sheet, so a mapped colour resolved to nothing and a stroke with no value computes to none: the component
  diagram's HTTP edges and arrowheads vanished. Each mapping now falls back to the colour the engine painted, and the
  link rule to the engine's link blue, so a page without the theme draws as it did before 5.0.0. The activity
  diagram's colours, the component diagram's dark look and whether ComponentDiagram.html takes the theme are S11's
  (D26, D28; the S11 brief's open question). (1) and (2) were found by reading the component skin and the
  internal-flow markup while drafting S14's wiki list; (3) by the S11 research, and measured before the fix.
  Tests: Playwright `DiagramThemeColoursTests` gains two facts, a box filled in a dependency colour keeping it in the
  dark theme and an activity diagram's black text staying black there, and `ComponentDiagramPageTests` one, every line
  the engine drew still painted on a page without the theme. All three failed before: the box took rgb(106, 165, 228),
  the text rgb(228, 233, 240), and four HTTP paths and arrowheads computed no stroke. `checks/mutate-s10b-c.py` and
  `checks/mutate-s10c-a.py` read the rules' new form (all 23 of their strings checked against the files). Mutations: 3
  of 3 killed (`checks/mutate-s10b-e.py`).
  Checks: the S10 classes the fixes touch pass (34: `DiagramThemeColoursTests`, `DiagramRolesTests`,
  `DiagramExportLookTests`, `DiagramCentringTests`, `DiagramLinkHoverTests`, `ComponentDiagramPageTests`); the full
  suites run next, at the end of S10.
- 2026-10-19, S10's full suites, at the S10b-3 follow-up (`2c39cba2`): the core suite passes 7,755 with 45 skipped,
  and the full Playwright suite passes 1,217 with 28 skipped (the 28 of S9c's run). The core suite's skips were 2 in
  S9c's run: all 43 more were Node tests (40 in three classes, one each in three more), skipped because the test
  projects' node probe (`tests/Kronikol.Tests/NodeProbe.cs` and five copies, two of them in the end-to-end project)
  waited five seconds for `node --version` and then read the exit code of a process still running, which threw, which
  it took for "no node": on a loaded machine (the suite ran below normal priority beside three other sessions' builds)
  every Node test of the run skipped without a failure. Fixed on the branch in the same commit as this entry
  (`edits/fix-nodeprobe.py`): one probe per test project, waiting a minute and stopping a node that never answers,
  every copy routed to it; `NodeProbeTests` (new, 3: a node that answers after six seconds still answers, one that
  never answers is stopped and does not count, one that fails does not count) failed before at the missing member. The
  Node classes then pass: the probe's facts and the nine classes that had skipped run 450 tests, every one passing and
  none skipped; one mutation, the wait capped at five seconds again, is killed (`checks/mutate-nodeprobe.py`). The
  probe is the same on main (a 4.x patch, for the owner).
- 2026-10-19, S11a: the colours carried from S10 mapped to the theme's tokens (the preview's `c-round5.css` 90-110 and
  `c-round17.css` 119-121, sp P36.2, P36.10-12, P36.16, P36.21), in `edits/scripts-s11a.py`, each falling back to the
  engine's colour: an activity diagram's ground, ink, lines, arrowheads, boxes (`#F0F4FF` edged `#666666`) and lanes
  (`#CCCCCC`), in an `.iflow-diagram` box, which holds both a scenario's whole-test flow and the internal-flow popup's
  diagram; the component diagram's person box (`#08427B`), its database box's edge (`#09616A`) and its edge labels
  (`#666666`), scoped to `#component-diagram`, since `#666666` is an activity's edge too; and a sequence note's header
  line (`NotePalette.HeaderInk`, `#686868`). The pinned engine writes each as a six-digit attribute (probed). S10b-3
  follow-up's interim fact, an activity diagram's black text staying black in the dark theme, is replaced by the facts
  that it takes the theme, and its mutation, which no longer changes anything a test can see, is removed.
- Not ported: the preview's soft ink for `#666666` and `#686868` text inside an activity diagram, which no activity
  diagram Kronikol draws carries.
  Tests: Playwright `DiagramActivityAndComponentColoursTests` (new, 4 theories, 8 cases in both themes: the whole-test
  flow's ground, ink, lines, arrowheads, boxes, edges and lanes; the popup's boxes and ink; the component diagram's
  person box, database edge and labels; a note's header line), failing 8 of 8 before the change. A lane's colour is
  drawn only between two swimlanes (Kronikol's `SwimlaneBorderColor #ccc`; a flow whose spans share one source draws
  one lane and no line), so `ReportTestHelper.GenerateRunReportWithFlowArrows` gains an opt-in second source
  (`twoSources`) that the theories ask for. Mutations: 11 of 11 killed (`checks/mutate-s11a.py`, which now takes part
  of a test name): the component diagram's and the note header's four in a first run, the whole-test flow's seven
  again after the lane fix, since the first run's flow mutations read a theory that failed without them.
  Checks: the new class and the classes it touches pass, 41 of 41 (`DiagramActivityAndComponentColoursTests`,
  `DiagramThemeColoursTests`, `DiagramRolesTests`, `ComponentDiagramPageTests`, `ArrowLinkOpensPopupTests`,
  `DiagramCentringTests`).
- 2026-10-19, S11b: the flame chart in the diagrams' manner (D44, the owner's round 17 item 10; `mock/build.js`
  238-255 and `mock/c-round17.css` 98-110), in `edits/scripts-s11b.py`. A source takes one of eight hues (blue,
  violet, teal, rose, green, amber, purple, coral), written as `--fh` on its bar by the page's flame script and by
  `InternalFlowRenderer` alike (`FLAME_HUES`, `FlameHues`), where any hue from 0 to 359 at 60% saturation was
  lightened with depth. The popup sheet draws the bar from it: a pale fill and a 3px edge in a stronger shade, ink
  text, 20px rows 2px apart and 4px corners. The fill, edge and hover shades are tokens (`--dg-flame-fill`,
  `--dg-flame-edge`, `--dg-flame-hover`) with dark values in both dark blocks of the token sheet, not the preview's
  `:root[data-theme="dark"]` rules, and the bar's rules fall back to the light shades where a page has no token sheet.
  The flame data keeps its depth field (its format is the report's); the script's `buildBar` and the renderer's
  `RenderFlameNode` no longer take one (F7).
  Tests: Playwright `FlameChartPaletteTests` (new, a theory in both themes: the bar's hue is one of the eight, its
  fill and edge are that hue at the theme's shades, 3px edge, ink text, 20px rows 2px apart, and a lightness inside
  the theme's range, since a dark page drawing the light shades would still match the tokens it reads; failing both
  cases before the change, at the missing hue), and core `CultureInvariantOutputTests`' flame fact reads the palette
  hue (`--fh:`) where it read `background:hsl(h, 60%, ...)`, failing before. Mutations: 8 of 8 killed, among them the
  dark theme left with the light fill, which only the lightness check sees (`checks/mutate-s11b.py`).
  Checks: the core flame and internal-flow classes pass (277); `FlameChartPaletteTests` passes in both themes. S11a's
  lane fix (`edits/fix-s11a-lanes.py`: the flow-arrows fixture's opt-in second source) is committed with S11b.
- 2026-10-19, S11c: the internal-flow popup on the theme's tokens (the preview's `c-round5.css` 178-183, `c-dark.css`
  93, `c-contrast.css` 24-30, `c-extra.css` 195 and `c-round17.css` 107-110; stylesheet inventory row 18), in
  `edits/scripts-s11c.py`. The card, heading, close mark, messages and failure text, the call tree, test bands,
  relationship list and table, notes buttons, the flame chart's zoom hint and step boundaries, and the link hover take
  the theme's colours, each falling back to its 4.x colour where a page has no token sheet (the test pages of
  `TestPageGenerator`); the overlay is a new token, `--kron-overlay` (.4 light, .6 dark, in both dark blocks). The
  Activity and Flame Chart switch is a pair of tabs drawn as a scenario's diagram tabs are. The flame chart's labels
  are set in the monospace face with 6px padding, and its step boundaries are the ink at 16% and 45%. The popup's
  error boxes (`internal-flow-popup-script.js`) and the generator's notes (`InternalFlowHtmlGenerator`'s left-out
  note, both diagnostic blocks and the "more tests" row) lose their inline colours for classes.
  Deviations: every rule keeps its 4.x selector, since a custom `InternalFlowPopupCustomStyleSheet` follows the
  built-in sheet and wins on order only (`ReportGeneratorStyleOrderTests` sets `.iflow-toggle-active` in one); the
  preview's `.iflow-toggle-btn.iflow-toggle-active` would have beaten it. The preview's ink rule on `.iflow-popup` is
  left out: the popup inherits the page's ink (`body` is `var(--kron-ink)`, and no theme sets another), so the rule
  changed nothing anywhere Kronikol draws. A page without the token sheet draws the step boundaries at 16% and 45%
  black, where 4.x drew 30% and 60% (no test page shows them).
  Tests: Playwright `IflowPopupThemeTests` (new: the card, ink, heading and close mark equal their tokens and the
  overlay darkens to .6, in both themes; the tabs take the accent and the quiet ink on a page that sets them,
  unfilled, the chosen one underlined), `IflowPopupTests`' style pins turned to the tabs, and `SpansLeftOutPopupTests`
  reading the left-out note's colour (the theme's muted ink, no style attribute), failing 5 of 5 before the change
  (run with the four sources stashed), and core `PopupNotesByClassTests` (new, 3 facts, 4 cases: the left-out note,
  both empty-popup diagnostic blocks and the more-tests row written by class with no inline style) failing 4 of 4
  before it. Mutations: 11 of 11 killed (`checks/mutate-s11c.py`). The changelog's look entry says it; the Kronikol4J
  ledger owes a line for the generator's classes.
  Checks: the popup's classes and their neighbours pass, 72 of 72 (the Iflow, InternalFlow, SpansLeftOut, FlameChart,
  ArrowLink, DiagramActivityAndComponentColours and ComponentDiagram classes), and the core classes it touches, 518 of
  518 (the InternalFlow and ComponentDiagram namespaces, `StylesheetRulesTests`, `ReportGeneratorStyleOrderTests`,
  `ReportThemeTests`, `CultureInvariantOutputTests`).
- 2026-10-19, S11d: the Timeline tab's controls (D27; the preview's `c-layout.js` 338-450, `c-extra.css` 102-106,
  `c-round3.css` 60-74, `c-round4.css` 40, `c-round5.css` 141, `c-contrast.css` 32-36 and 48-55; stylesheet inventory
  row 19), prepared in the session's scratch and applied with `edits/scripts-s11d.py` and `edits/tests-s11d.py`. The
  header line holds Show (Filtered, the start, or Full) and, where a row paid a first-call warm-up, Warm-up (Shown or
  Hidden) and Order (With or Without warm-up); a note says how many rows Filtered leaves out. The behaviour is
  `report-timeline-script.js` (new, loaded only where the timeline is): rows follow each change of the filters and a
  link's filters as the page opens, bars are drawn to the longest row showing, and with the warm-up hidden a bar and
  its time are the scenario's own. A failed scenario's row opens with a cross; the timeline's rules are on the tokens.
  Deviations: rows name their scenario by `data-sid`, not `data-stable-id`, since a `#sid-` link resolves to the first
  match not hidden by `style.display` and a row in a hidden panel passes that check (`StableIdDeepLinkTests` also
  counts the attribute's copies); the times keep their attribute names, so `MergeReadsDurationsTests`' count of
  `data-duration-ms` is anchored on the scenario's element and the row; the page formats only own times, so every
  other time stays the generator's text; the note shows only while the timeline draws fewer rows than it has (the
  preview showed it whenever the filters hid a run); the phone rule is the report's 768 px, not the preview's 600 px;
  the bypassed bar is blue, as the run's ring draws it (owner question).
  Preview defects not ported: rows matched to scenarios by name, seconds read from the bar's title by a pattern that
  cannot read "1m 5s" (a row of a minute or more kept its width, fell out of the scale and sorted last), and
  `fmtSeconds` with no minute form.
  Tests: core `TimelineControlsTests` (new, 9 facts) and Playwright `TimelineTabTests` (new, 11 facts, 12 cases),
  failing 12 of 12 before the change (run with the three sources stashed; four time out waiting for a control that is
  not there yet), and the core facts with the moved pins failing 11 of 11; three pins move (`WarmUpHtmlTests`' legend,
  `MergeReadsDurationsTests`' count, `WarmUpMarkTests`' report as a static `WriteOrders`). Mutations: 12 of 12 killed;
  the note never showing survived its first aim, the two-features fact, which read the note's text only, so that fact
  now checks the note shows, and kills it (`checks/mutate-s11d.py`).
  Checks: the timeline's neighbours pass, 142 of 142 (the Timeline, WarmUp, Duration, StableId, ReportTabs,
  ReportToolbar, ViewportSweep and Filter classes), and the core Reports namespace, 2,736 of 2,737 with 1 skipped.
- 2026-10-19, S11f: the History section's rings and timed bars (R17.29, R17.30; the preview's `c-round2.css` 89-116,
  `c-round7.css` 86-91, `c-round4.css` 9-12 and `c-round17.css` 165-181; stylesheet inventory row 21), prepared in the
  session's scratch and applied with `edits/scripts-s11f.py` and `edits/tests-s11f.py`. The pass rate is a ring per
  run through the run band's ring code (`RingSvg` and `RingRate`, the run band's bytes unchanged), floored and never
  100% while any scenario did not pass; the duration is an HTML bar per run carrying its time, in the bar or above one
  under 30% of the chart, scrolling in its card and opening on the newest run. `HistoryHtml` emits it all, so the labs
  page draws it too; the card and every verdict kind are on the tokens with light fallbacks. The run band's formatter,
  shared with the bars, gains an hour form ("1h 5m" in the run band and features table, where they wrote "65m 0s").
  Deviations: D47's hatch is not ported (round 17 hid the chart it hatched); the bars use the run band's formatter,
  not `FormatDurationBadge` (which writes a tenth under a minute and minutes past an hour); 48 px columns and an rtl
  scroller for a full window of 51 runs (the preview's fixture had 5); every verdict kind keeps its 4.x hue on tokens
  (the preview's red `new` and amber `slower` are not ported, nor its `.history-verdict-faster`).
  Preview defects not ported: the convert script that rebuilt the charts from tooltips, the rounding `short()` (which
  could write "1h 60m" and "1000ms"), and the `[style*="height: 8%"]` rule that left bars between 8% and 28% with a
  time across their edge.
  Tests: core `HistoryRingsMarkupTests` (new, 11 facts, 27 cases) and Playwright `HistoryRingsTests` (new, 5 facts, 6
  cases), failing 8 of 10 before the change in Playwright (run with the three sources stashed; the two that passed are
  `HistorySectionTests` facts the change leaves alone), and the core facts 28 of 28; five pins move in
  `HistoryHtmlTests` and `HistorySectionTests`. Found while preparing the mutations: no case of the rate theory told a
  floored rate from a rounded one (every case hit the 99 cap or a whole share), so a case was added before applying (2
  of 3 reads 66%). Mutations: 12 of 12 killed (`checks/mutate-s11f.py`; the 99 cap is not mutated, being equivalent
  while the rate is floored).
  Checks: the History section's neighbours pass, 60 of 60 (the History, RunBand, LabsPage, ReportTabs,
  ReportSectionWidth, ViewportSweep, FeaturesTable and Verdict classes), once two test faults that run found were
  fixed: the labs page's fact read the first ring, a failed run's, whose rate is in the failure ink, and now reads a
  run that passed; and `HistorySectionTests` read the list headings with innerText, which the new uppercase labels
  change, and now reads their text. The core History and Reports namespaces pass, 3,052 of 3,053 with 1 skipped.
- 2026-10-19, S11g: the report's diagnostics on the theme's tokens (the preview's `c-round2.css` 89-92 and 117-120,
  `c-round7.css` 87 and 92; stylesheet inventory row 22), in `edits/scripts-s11g.py`: a card on the card ground with a
  hairline edge and 12px corners, the heading no longer drawn as a disclosure in its tab (the tab is one, S3) but
  still one on the labs page, which has no tabs, one rule between entries where each entry carried a rule under it,
  and each kind's pill in its own colours (a render, output or attachment failure in the failure colours, a degraded
  capture in amber, the rest quiet). Every colour falls back to its 4.x value where a page has no token sheet.
  Deviations: not ported (stylesheet inventory 3.8 items 2 and 3), the preview's doubled separators and every kind
  drawn amber.
  Tests: Playwright `DiagnosticsCardTests` (new, a theory in both themes: the card's ground and edge equal their
  tokens, a render failure's pill is in the failure colour and a malformed line's is not, no rule over the first
  entry, a rule over the second and none under any, the heading with no marker and no pointer in its tab; and the labs
  page's diagnostics still a disclosure in their 4.x colours, a guard that passes before the change), failing both
  theme cases before the change, the labs page's guard passing, as a guard does. Mutations: 8 of 8 killed
  (`checks/mutate-s11g.py`).
  Checks: the diagnostics' neighbours pass, 29 of 29 (the DiagnosticsCard, Diagnostic, ReportTabs, LabsPage and
  ReportSectionWidth classes).
- 2026-10-19, S11e: the Components tab's heading and controls (D26, D28; the preview's `c-layout.js` 452-555 and
  569-576, `c-round9.js` 94-102, `c-extra.css` 103 and `c-round4.css` 9), prepared in the session's scratch and
  applied on the merge of origin/main (`9bb77de6`) with `edits/scripts-s11e.py` and `edits/tests-s11e.py`. The tab is
  headed by `ComponentDiagramOptions.Title` ("Component Diagram" when blank) with the timeline's info mark, and the
  run report's drawing leaves the title out (`ComponentDiagramGenerator.GenerateEmbedded`; `ComponentDiagram.html`
  keeps it, and `GeneratePlantUml`'s bytes are unchanged). Where the page draws the diagram the tab has S11d's
  segmented groups: Show (Filtered or Full), Direction (Left to right or Top to bottom) and Call summaries (Shown or
  Hidden), with S11d's note while Filtered draws less than the whole run. Filtered follows which scenarios made each
  connection's calls, read from the run's calls (`DrawnCalls`, the filter `ExtractRelationships` used, and `TestsOf`)
  and handed to the page as a JSON model of the source's lines and the scenarios' stable ids, so nothing is matched by
  name. `report-components-script.js` (new) builds each form from the original in `puml-data`, hands the first to the
  renderer before it draws, so a link's filters reach the first drawing, and draws later forms out of sight and swaps
  them in, dropping a render a newer one overtook; a change while the tab is hidden is drawn when it opens. A run
  whose calls give the diagram no connection has no Components tab. `kronikol merge` builds the model from the shards'
  calls when they carried any, and has Show only then.
  Deviations: the configured title is the heading (the preview's was fixed); no `_preProcessSource` hook (pages that
  draw when the report is written do not load collapsible-notes, and every toggle skips this diagram); no patterns
  over drawn labels; no emptying before a redraw; "the 1 scenario" in the singular; the counts sentence only while the
  counts show. Not ported: the preview's two `.component-header` rules, which cancel out.
  Tests: core `ComponentsTabHtmlTests` (new, 12 facts, 14 cases) and Playwright `ComponentsTabTests` (new, 11 facts,
  12 cases); `OnDemandRenderingReportTests`' Server panel fact is rewritten to decode both image addresses and hold
  them equal but for the title line. Red with the tests and the generator stage applied: the core class 12 of 14 (its
  two generator facts pass, their red being the missing members) and the rewritten fact, and the Playwright class 12
  of 12. Two test faults found on the way and fixed before green: the renderer theory's Local case asked for Svg files
  with no image directory, which throws whatever the report holds (now Base64Svg for Local), and the head check found
  the "</head>" that `report-export-function.js` carries in its text (now read from the parsed head). Green: the core
  class with every `OnDemandRenderingReportTests` fact, 32 of 32, and the Playwright class, 12 of 12. Owner questions
  (six): the configured title as the heading; no tab for a run with no connection; the empty-state wording; Copy
  source giving the form drawn; no start option; the title left out under NodeJs, Server and Local. Mutations: 18 of
  18 core mutations killed, the 11 Playwright ones to run with S11's full suites once the other sessions' Playwright
  runs end (`checks/mutate-s11e.py`; the drop of a render that lands after a newer one has no deterministic browser
  fact and is held by `checks/components-script-check.js` under node, which three mutations of the script each fail).
  Checks: The full suites run on v5 with this commit and the 11 Playwright mutations, recorded with S11's status.
- 2026-10-19, S9a fix: the doc comment on `ReportGenerator.SetupMarker` named `StepBarPlantUml` from
  `Kronikol.Reports`, where the compiler could not resolve it (CS1574 at ReportGenerator.cs 1137, a warning in every
  build since S9a); it names `PlantUml.StepBarPlantUml` now (`edits/fix-s9a-cref.py`). Red: the warning in the S11e
  builds. Green: a build of the library with no CS1574 and no warning from ReportGenerator.cs.
- 2026-10-19, S12 phone layout, part 1: one fact holds the phone breakpoint to one value everywhere (the brief's "a
  core fact pinning one breakpoint across sheets and JS"), added with `edits/tests-s12p1.py`:
  `StylesheetRulesTests.The_phone_breakpoint_is_one_in_every_sheet_and_every_script` reads every width bound from 760
  to 780 px in every sheet (the token sheet too) and in every embedded script's `matchMedia`, and requires `max-width:
  768px` or `min-width: 768.02px`. The fact before it read only the sheets' `min-width` bounds, so a script asking at
  769 px, or a phone block written as `max-width: 769px`, passed. Every rule holds today (no 769 in `src/`), so the
  fact's red is its mutations: 4 of 4 killed (`checks/mutate-s12p1.py`: two scripts and two sheets moved off the
  breakpoint).
  Checks: the StylesheetRules class passes, 29 of 29.
- 2026-10-19, v5 test faults after the merge and S11c (found by kronikol-89's full Playwright run on `9bb77de6`),
  fixed with `edits/tests-v5-fixes.py`: `AttachmentRenderingTests`' three helpers from main (4.29.0, 4.31.1) clicked
  4.x's "Expand All" buttons, which v5 replaced in S6, and use `ExpandAllFeaturesAsync` and `ExpandAllScenariosAsync`
  now (six facts); `ToolbarStylingTests`' popup theory held 4.x's literal colours for the default theme, which S11c
  moved onto the tokens with the view switch as tabs, and holds the tokens now (only its first check had ever run).
  Red: 7 of 17 failed before it (the six attachment facts, each after a 30 s wait, and the popup theory's default
  case). Green: 17 of 17 (the AttachmentRendering class and both cases of the popup theory). Lesson: a stage's
  neighbour checks pick the classes by name, and this class's name gave no hint it reads the popup; the full suite
  after each stage is what catches such a test.
- 2026-10-19, S12 D43: the diagram menu's "in new tab" items open the tab in the click (R17.21; the preview's
  `c-round17.js` 1-29 and `build.js` 203-226; `script-patches-and-diagrams.md` P13), applied with
  `edits/scripts-s12d43.py` and `edits/tests-s12d43.py`. The helpers sit inside `context-menu-script.js`'s wrapper,
  not on `window`: `openTab` opens `about:blank` while the click is handled (`createMenuItem` runs the action in the
  click), `writeTab` writes a page into it, `openPngInTab` writes the canvas as a PNG data URL once it is drawn, and
  `openSvgInTab` and `openTextInTab` write an SVG data URL or the escaped text; each falls back to the `blob:` URL
  when no tab opened. All nine sites go through them.
  Deviations: the box text's tabs are titled "Box text" and a source's "PlantUML source" (the preview titled every
  text tab "Diagram source", its F12).
  Tests: Playwright `OpenInNewTabTests` (new, 3 facts, 4 cases: the tab opens before the picture is drawn, its title,
  an image from a data URL that loads, and a source written as text equal to the diagram's) and
  `NoteCopyFidelityTests`' box-text fact (its tab's title, and no `blob:` URL); core `DiagramContextMenuTests`' two
  facts turned round (they held "blob URLs, not data URIs"), failing 2 of 2 before the change. Red with the menu
  script at its last commit: the four OpenInNewTabTests cases and the box-text fact fail, each tab's title never
  coming. Green: 4 of 4, with the box-text fact and the menu's neighbours (ContextMenuExtended, IflowPopup,
  NoteCopyFidelity, DiagramExportLook). Mutations: 7 of 7 killed (checks/mutate-s12d43.py). Found while running them:
  the first green ran on a stale build (the chain put the script back with a copy that kept its old time, so the build
  skipped the compile); the order check first recorded toDataURL, which comes after the tab opens even when the tab
  opens late, and records drawImage now, which killed the two mutations that had survived; and the source fact's
  fixture held no markup, so the fact now writes some into the source it opens.
  Checks: the context menu's core tests pass, 130 of 130 (the DiagramContextMenu, ContextMenu and
  NoteCopyFidelityScript classes). The full suites run on v5 with this commit.
