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
   `FeaturesSummaryOpen` is S4's (the Summary view).
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

