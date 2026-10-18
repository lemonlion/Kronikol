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
| R17.23 | per-scenario settings hidden by default | `Scenario settings` switch on the tools line (D23) |
| 25 | toggles change width while loading | fixed padding, the sweep shown, labels reserve their bold width |

## 4. Stages

Each stage is one or more commits on `v5`, tests first, with the preview's checks run against the branch's
rendering of the parity run at the end of the stage. The order follows the structure inventory's section 7.

| Stage | Content | Needs |
|---|---|---|
| S0 | Parity harness: the parity run, its rendering by the branch, the preview rebuilt over the same run (section 6) | done 2026-10-18 |
| S1 | Stylesheet foundation: token sheet (light, dark under `[data-theme]` and `prefers-color-scheme`), `[hidden]` rule, sticky bar; theme boot script, title line and theme switch (D1) | Q1 |
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
   (`PersistentFilterStateReportTests` pins that the page never mentions `localStorage`). The theme and Scenario
   settings switches want to be remembered. Recommendation: allow it for those two display preferences only, never
   for filters or toggles, and narrow the test to say so.
2. **Options that name sections now tabs** (`ScenarioTimelineVisible`, `ComponentDiagramVisible`,
   `FeaturesSummaryOpen`, `DiagnosticsOpen`). Recommendation: keep the names; `...Visible` decides whether the tab
   exists and `...Open` which tab or view is showing when the page opens; documented for 5.0.0.
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
