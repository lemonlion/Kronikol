# Toolbar at every width plan: stage 1.5 (toolbar plan §2.1, F5, F6)

**Date:** 2026-09-22, revised the same evening (§2.6 to §2.8, S1b, S5, the census), second pass
2026-09-24 (§2.9 to §2.14, the C# prototype, the published reports; D5's recommendation moved to
1160 px) · **Repo version:** 3.29.0 (`753b8790`) for the second pass. Pages generated there are
byte-identical to the first pass's (3.27.2, `2843018a`) apart from the version stamp and the run's
clock, so every first-pass RUN claim still holds. The checkout has since moved to 3.29.1 (`afeafa5b`),
which changes none of the report, stylesheet, generator, E2E or CI files this plan measures or cites
(only the version number, on the same lines, and other plans' index rows) · **Status: EXECUTED 2026-09-24 as 3.29.2**
(§9: what shipped, what departed from this plan and why), with D5 taken at the recommended 1160 px and Q6 wired; Q7
and the dead parameterized-row hover executed the same day as 3.29.3 (§9, its second part). Before execution a C# prototype was built and tested in a throwaway worktree (§2.14, the harness's
`prototype.diff`). This is
P2 of [`STAGE_1_PLAN.md`](STAGE_1_PLAN.md): roadmap item 1.5, track B
(`src/Kronikol/Reports/stylesheets.css`, `internal-flow-popup-styles.css`, `Constants/Stylesheets.cs`,
the `<style>` block `ReportGenerator.cs` emits and its two report call sites, and
`tests/Kronikol.Tests.EndToEnd/`). It needs **D5** answered (§10 Q1). Every number below was measured
with the harness in [`TOOLBAR_AT_EVERY_WIDTH_PLAN.harness/`](TOOLBAR_AT_EVERY_WIDTH_PLAN.harness/);
§1 says which claim rests on which run, and the harness README holds the full tables.

Covers `TOOLBAR_REDESIGN_PLAN.md` §2.1 (the two shipped bugs: export labels wrapping inside their
buttons, and the report scrolling sideways between about 769 and 1000 px), finding **F5** (the
`.diagram-toggle` base styles ship only with internal-flow tracking) and finding **F6** (the violet
theme has no active-hover rule for the diagram tabs), all recorded in the investigation of
2026-08-31 and re-checked at HEAD by the roadmap on 2026-09-22.

**What the grouping asked for, and what this plan does with it.** `STAGE_1_PLAN.md` said: no-wrap on
the export buttons; min-width on the search input; the minimal band wrap below about 1000 px that D5
decides; the toggle base styles moved out of the internal-flow stylesheet so the toolbar is styled
without flow tracking; the violet active-hover rule; the 320 to 1400 px sweep as a permanent
Playwright guard, because the existing responsive tests spot-check four widths; one Kronikol4J ledger
entry, since the stylesheet is byte-shared. All of that is kept except the search input's min-width,
which the second pass measured to do nothing (item 11). What the grouping did not know, each measured
(§2). First pass:

1. **F6 is the smaller half of a bigger defect.** The custom stylesheet is emitted *before* the four
   component stylesheets, so every theme rule aimed at a control those sheets style loses at equal
   specificity. On every default `Specifications.html` the active Details radio paints Google blue
   inside a violet report, and under flow tracking the violet tab, flow-toggle and related-list rules
   lose at rest, not only on hover. Adding a hover rule to the violet constant, which is what F6 asks
   for, fixes nothing until the sheet moves after the component sheets (S3).
2. **`white-space: nowrap` on its own moves the defect rather than removing it.** With the labels
   unwrappable the export cluster escapes the filtering box through 1070 px on a CI-run report, and
   the page still scrolls sideways from 770 to 920 px. The cluster and the box header have to wrap as
   rows too (S1), and the band wrap D5 decides is what clears the rest (§2.5).
3. **F5's fix needs one declaration from F11.** Making the toolbar a flex row in every report, which
   is what F5 asks for, gives every report the in-button label wrap that flow-tracking reports have
   today. `flex-wrap: wrap` on `.diagram-toggle` at every width, one line of F11, is therefore a
   precondition of F5, not an extra (S2). Item 8 shows it is also a fix in its own right.
4. **A long branch name defeats every rule above.** The CI box is `flex-shrink: 0` and its cells never
   wrap, so a 69-character branch scrolls the page sideways from 780 to 1320 px at HEAD, and still
   from 1120 to 1200 px with S1 applied. A width cap on the box in the row layout clears it, and
   changes nothing on a short branch (S1b, §2.6).
5. **The violet theme has a defect of its own order.** Its hover block for the four filter toggles
   comes after their active block, so an active filter toggle turns pale on hover on every violet
   report today. On a phone, where a tap leaves the control hovered, the filter just tapped shows
   white text on pale lavender until the next tap elsewhere (§2.4). The same trap waits for every
   hover rule S4 adds; they go before the active rules. A full census of the blue tints on a violet
   page found eleven more the theme misses.
6. **A documented stylesheet option has never been applied.** `InternalFlowPopupCustomStyleSheet`
   (`ReportConfigurationOptions.cs:181`, two wiki pages) is read by nothing; the Kronikol4J ledger
   recorded that a year ago as a reason not to port it (S5).
7. **The sweep measured a hidden toolbar on phones.** At 768 px and below the init script hides every
   scenario toolbar behind a "Diagram Settings" button, so a sweep that does not open it passes
   vacuously there. The guard opens it (§4).

Second pass (2026-09-24):

8. **The scenario toolbar's controls are cut off, not only squeezed.** `.feature` and `.scenario`
   carry `content-visibility: auto` (`stylesheets.css:28`, `:38`), whose paint containment clips
   whatever overflows them, and the page's own scroll width never shows it. A toolbar carrying every
   control a report can give it (the Sequence, Activity and Flame tabs, Assertions, Steps, Databases,
   the note selects) has six controls out of sight at 780 px and the last one still at 1200. On the
   published BreakfastProvider reports (3.29.0, 158 toolbars of up to ten buttons) controls are
   clipped from 780 to 1300 px, up to 522 px of them, on the run report and on `Specifications.html`
   alike. The first pass's fixture had five controls and only squeezed them. S2's `flex-wrap` clears
   it; the guard needs an assertion the page's scroll width cannot give (§2.9, §4).
9. **Every default run report's top bar wraps its labels between 500 and 580 px.** The component
   diagram is embedded by default (`ComponentDiagramOptions.EmbedInTestRunReport`, `:14`), which puts
   a fourth button in `.toolbar-left`, a flex row that wraps only at 480 px and below. The fixture had
   no component diagram. One declaration, added to S1 (§2.10).
10. **`min-width: 769px` leaves a gap real browsers land in.** Firefox at 125 % and at 175 % gives a
    769 px window a viewport of 768.8 or 768.97 CSS px, which matches neither `max-width: 768px` nor
    `min-width: 769px`: the band is skipped and the prototype scrolled sideways by 158 px there (431
    on a long branch). The lower bound is written `768.02px`, which closes it (§2.12).
11. **Two things the first pass wrote down were wrong, and one design was heavier than it needed to
    be.** The search input already shrinks with the box (15 px wide at 780 px at HEAD: its `width: 100%`
    caps its automatic minimum), so `#searchbar { min-width: 0 }` does nothing and is dropped. S3 as
    worded would have cost every report without a custom sheet one byte, a newline; §3.3 now says
    which line keeps it. And S5 needs no new parameter on the public `GenerateHtmlReport`: the two call
    sites compose the sheets (§3.7), so Q6's semver question goes away.
12. **The conditions the first sweep held fixed move the bands, and one moves D5.** A classic 15 px
    scrollbar (Playwright hides it), a window resized without a reload, Firefox 148 and WebKit 26.4,
    and the WCAG 1.4.12 text-spacing override were each measured (§2.11). The prototype is clean
    under all of them at a 1160 px breakpoint. At 1100 px the text-spacing override still scrolls the
    page sideways between 1110 and 1160 px, which is why the recommendation moves (§10 Q1).

---

## 0. Summary

Two report shapes plus a stress shape and a full-toolbar shape, the three published reports, ten
rules, one moved block, one reordered block, one composed argument, one ordered constant, one E2E
class, one patch.

**What is wrong, measured on a report shaped like the published one** (six scenarios, seventeen
dependencies, CI metadata; §2.1) and on the published reports themselves (§2.13): the three export
buttons wrap their labels inside themselves at 380 px and below and again from 770 to 1260 px; from
770 to 1060 px the page scrolls sideways, by 295 px at 770, because the filtering box is squeezed to
32 px there (125 px at 900, 225 at 1000) and the export cluster pushes out of it (the published run
report: 780 to 1080 px, 310 px at peak); with a 69-character branch name the scroll runs from 780 to
1320 px and peaks at 558 px (§2.6); without flow tracking the scenario toolbar is a `display: block`
div whose tab buttons carry no rule at all; with flow tracking it is a flex row that does not wrap,
so its labels wrap inside their buttons and, with a full toolbar, its last controls are clipped out
of sight from 780 to about 1300 px (§2.9); the top bar's labels wrap inside their buttons from 500 to
580 px on every run report with the default component diagram (§2.10); the violet theme's rules for
the Details radio, the diagram tabs, the flow toggle and the related-tests list are dead or half-dead
because the theme is emitted first; the theme's active filter toggles lose their colour on hover; and
thirteen controls the base sheets tint blue have no violet rule at all (§2.4).

**What this plan does.** S1: four declarations in `stylesheets.css` (`.export-btn { white-space:
nowrap }`, `.filtering-box-export { flex-wrap: wrap }`, `.filtering-box-header { flex-wrap: wrap }`,
`.toolbar-left { flex-wrap: wrap }`) and one media block (between `768.02px` and the D5 breakpoint,
`.header-row { flex-wrap: wrap }` with `.filtering-box { flex-basis: 100% }`). S1b: the CI box capped
at 20 em in the row layout, its value cells allowed to break. S2: the six `.diagram-toggle*` rules move
from the popup stylesheet into `stylesheets.css`, and the toolbar gains `flex-wrap: wrap`. S3:
`ReportGenerator` emits the custom stylesheet after the component sheets, still before `CustomCss`,
byte-identical when there is none. S4: the violet constant's hover rules move above its active rules,
and it gains the hover and active rules the census found missing on toolbar controls (Q3 names four
non-toolbar ones the owner may add). S5: `InternalFlowPopupCustomStyleSheet` is composed after the
theme, before `CustomCss`, when flow tracking is on. A prototype of exactly this, first as string
surgery on generated pages and then as a C# change built in a worktree, clears every assertion at
every width from 320 to 1400 px on nine synthetic shapes and on the three published reports, under a
classic scrollbar, without a reload in both directions, in Firefox and WebKit, under a wider font and
under the WCAG text-spacing override, and paints every probed control violet on the violet pages while
leaving the blue report unchanged; the repo's unit and E2E suites pass on it unchanged (§2.5, §2.11,
§2.13, §2.14).

**What guards it.** `ViewportSweepTests`, a new Playwright class in its own Chromium launched with
classic scrollbars: three generated pages with the long CI strings and every toolbar control, every
width from 320 to 1400 px in 20 px steps plus the two band edges, reloaded at each, the phone
toolbars opened, asserting no sideways scroll, no label wrapped inside any toolbar button, the export
cluster inside its box, every scenario-toolbar control inside its toolbar, and the header composition
D5 chose (§4). Plus unit pins on where each rule lives and on the lower bound, on the order inside the
violet constant and on the style order, an E2E cascade test that reads painted colours, and a phone
test that taps a filter (§5).

**One patch release** (§7). Report output changes, so one Kronikol4J ledger entry; nothing is
mirrored unless D11 says mirror (§6). D5 is the only decision; §10 recommends the breakpoint at
**1160 px** and says what 1100 and 1000 cost. Q6 asks whether S5 rides along; Q7 names a sibling
defect the second pass found in the parameterized tables and proposes it goes elsewhere.

---

## 1. How far each claim was checked

- **RUN**: executed and the output read. First pass against `2843018a` (3.27.2) on 2026-09-22;
  second pass against `753b8790` (3.29.0) on 2026-09-24, where `gen.cs` regenerated the five shapes
  and `bytediff.py` found them identical to the first pass's apart from the version stamp and the
  clock. The harness is `TOOLBAR_AT_EVERY_WIDTH_PLAN.harness/`: `gen.cs` (a .NET 10 file-based app
  referencing `src/Kronikol/Kronikol.csproj`) writes the synthetic shapes through
  `ReportGenerator.GenerateHtmlReport` (`-- stress` the long CI strings, `-- full` every toolbar
  control); `sweep.js` (first pass) and `sweep2.js` (second pass: engines, scrollbars, no-reload
  modes, injected CSS, clipped-control and clipped-content columns) open each page through the E2E
  project's own Playwright driver (`tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package`,
  1.59.1, with Chromium 147, Firefox 148 and WebKit 26.4 installed), reload at every width from 320 to
  1400 in 10 or 20 px steps unless a mode says otherwise, open every `<details>` and every
  phone-hidden toolbar, and measure; `cascade2.js` reads painted colours of thirty-seven control
  states; `rows2.js`, `probe-local.js` and `fractional.js` read geometry, clipping and media-query
  matching; `shots.js` and `tshots.js` take screenshots. Network is blocked except `file:`, so the
  engine fetch is not part of any measurement. The three published reports are the BreakfastProvider
  xUnit run report, its `Specifications.html` and its LightBDD run report, downloaded from
  `lemonlion.github.io/BreakfastProvider/reports/` on 2026-09-24 (generator 3.29.0, `a557133c`).
- **PROTOTYPE**: `patch.js` applies the §3 rules to copies of the generated pages by string surgery
  (the width rules appended as a style block; the toolbar base rules moved to the head of the main
  block; the violet block cut out and re-inserted after the component sheets with its hover block
  moved above its active block and the S4 rules around it), and the same probes run on the copies.
  It verifies the CSS and the order, not the C# that will produce them.
- **PROTOTYPE (C#)**: the second pass made the §3 change in the real source files in a detached
  worktree (`git worktree add`, removed afterwards), built it, regenerated every shape through it,
  byte-diffed and swept the output, drove S5 through `CreateStandardReportsWithDiagrams`, and ran the
  repo's unit and E2E suites on it. The diff is the harness's `prototype.diff` (4 files, +76 −29).
- **READ**: the code was read and the claim follows; nothing executed.

| Claim | Level |
|---|---|
| Pages generated at `753b8790` are byte-identical to those measured at `2843018a` apart from the version stamp and the clock | **RUN** (`bytediff.py out out-head`) |
| Export labels wrap inside their buttons at 320 to 380 px and at 770 to 1260 px with CI metadata (770 to 1110 without); not between 390 and 760 | **RUN** (harness §A, per-width data; the first pass's summary printed first and last width only and read "every width up to 1260") |
| The page scrolls sideways 770 to 1060 px with CI metadata (+295 px at 770, +165 at 900, +65 at 1000, +5 at 1060), 770 to 900 without (+141 at 770) | **RUN** (§A) |
| At every overflowing width the element past the edge is `.filtering-box-export` | **RUN** (§A, the culprit column) |
| The filtering box is 32 px wide at 770, 125 at 900, 225 at 1000, 325 at 1100 with CI metadata; 149 / 279 / 379 / 479 without | **RUN** (§A) |
| The export cluster sits outside the box from 770 to 1070 px with CI metadata, 770 to 910 without | **RUN** (§A) |
| At 768 px and below nothing overflows the page; the `Specifications` shape never overflows above 380 px | **RUN** (§A) |
| The search input is not a floor under the box: at HEAD it is 15 px wide at 780 px on the CI shape (98 without CI), and the prototype without `#searchbar { min-width: 0 }` measures the same as with it at every width in Chromium and Firefox | **RUN** and **PROTOTYPE** (§K; `width: 100%` on a flex item caps its automatic minimum, `.filter-search` is the flex row, `stylesheets.css:168`) |
| Without a component diagram the top bar wraps no label inside a button; with the default component diagram its four left-hand buttons wrap their labels at 500 to 580 px, at HEAD, after the first pass's prototype and on the published run reports | **RUN** (§A, §L, §N; `.toolbar-left`, `stylesheets.css:605`, wraps only in the ≤ 480 block, `:1890`; the button is emitted at `ReportGenerator.cs:1512` when `EmbedInTestRunReport`, default true) |
| With a 69-character branch and a 52-character repository the page scrolls sideways 780 to 1320 px (+558 at peak), the box is 32 px from 900 to 1000 and 152 at 1200, and the CI box is 547 px wide | **RUN** (§F, `gen.cs -- stress`) |
| With S1 alone that shape still scrolls 1120 to 1200 px (+83) with the cluster outside the box; with S1b it is clean at every width, the CI box 352 px, and the ordinary CI report's box stays 274 px | **PROTOTYPE** (§F) |
| Under Verdana on body and controls (wider than the Ubuntu runner's DejaVu Sans) the prototype is clean at every width | **PROTOTYPE** (§I, and again on the final design, §Q) |
| Without flow tracking `.diagram-toggle` computes `display: block` and its buttons carry no rule (UA default `rgb(240, 240, 240)`, no hover change) | **RUN** (§B) and **READ** (`ReportGenerator.cs:1180` gates the popup sheet on `internalFlowTracking`; the six rules are at `internal-flow-popup-styles.css:114-127`) |
| With flow tracking, the fixture's five-control scenario toolbar wraps labels inside its buttons from 770 to 890 px | **RUN** (§A; §H at 800, 850, 880: three labels wrapped, 39 px tall) |
| A scenario toolbar with every control (tabs, Details, Headers, Assertions, Steps, Databases, three selects) wraps labels inside its buttons from 780 to 1400 px and pushes controls past its own edge from 780 to 1280 px: six out of sight at 780 px, four at 1000, one at 1200, up to 505 px of controls; the page's scroll width does not show them | **RUN** (§L, `gen.cs -- full`, `probe-local.js`, screenshots) |
| The cause is `content-visibility: auto` on `.feature` and `.scenario` (`stylesheets.css:28`, `:38`): paint containment clips an overflowing descendant, and the clipped part adds nothing to the document's scroll width | **READ** (CSS Containment) and **RUN** (the clipped controls sit at x = 1244 in a 780 px window whose scroll width is 1065, set by the export cluster) |
| The fixture's five-control toolbar also clips a control at 780 to 800 px once a classic scrollbar takes 15 px | **RUN** (§M, the scrollbar sweep) |
| The published reports (3.29.0): the xUnit run report scrolls sideways 780 to 1080 px (+310), wraps export labels at 320 to 380 and 780 to 1280, top-bar labels at 500 to 580, scenario-toolbar labels at 780 to 1400, and clips toolbar controls at 780 to 1300 px (+522); its `Specifications.html` clips at 780 to 1300 px and never scrolls; the LightBDD run report reads like the xUnit one | **RUN** (§N) |
| The prototype on the published reports: no sideways scroll, no label wrapped, no toolbar control clipped, at any width | **PROTOTYPE** (§N) |
| What stays clipped on the published reports after the prototype is parameterized-test content: the grouped table (`display: table` above 768 px, 783 px wide in a 720 px scenario at 800) and, on the LightBDD report, the row detail panels at 320 to 760 px (757 px of content in 336 at 400) | **RUN** (§N; `.param-test-table` `stylesheets.css:1505` and `:1807`, `.param-detail-panels` `:1611`); Q7 |
| At 768 px and below the init script hides every scenario toolbar and inserts a "Diagram Settings" button; a sweep that does not open it measures nothing there | **READ** (`report-init-script.js:38-54`) and **RUN** (the sweeps open them) |
| The custom stylesheet is emitted before the context-menu, inline-SVG, collapsible-notes and internal-flow sheets | **READ** (`ReportGenerator.cs:1099-1102` builds `combinedStylesheet` = base + custom; `:1201-1207` places it first in the `<style>`) |
| On a default `Specifications.html` the active Details radio paints `rgb(66, 133, 244)`, not the violet `rgb(139, 92, 246)` | **RUN** (§B, §G, with and without flow tracking) |
| Under flow tracking a violet report paints the active diagram tab, the active flow toggle and the hovered related-tests item blue at rest | **RUN** (§B, §G) |
| Without flow tracking the violet active tab stays `rgb(139, 92, 246)` on hover and the idle tab has no hover tint | **RUN** (§B) and **READ** (`Stylesheets.cs:69` has the active rule and no hover rule) |
| On a violet page an active filter toggle paints the pale hover tint `rgb(237, 233, 254)` while hovered, where the blue report keeps its active colour | **RUN** (§G) and **READ** (`Stylesheets.cs:47-53` follows `:34-41` at equal specificity; the base sheet has hover at `stylesheets.css:373` before active at `:378`) |
| On an emulated phone (390 × 844, touch) a tapped "Happy Paths Only" filter is active and still `:hover`, and paints `rgb(237, 233, 254)` under white text until a tap elsewhere; the prototype paints it `rgb(139, 92, 246)` | **RUN** and **PROTOTYPE** (§P, screenshots) |
| On a violet page these hover or active states paint blue: dep-mode and cat-mode hover, percentile idle hover, the active percentile's border, collapse-expand-all hover, the timeline toggle's hover, active and active hover, export hover, the Details radio's hover, active and active hover, the phone "Diagram Settings" hover, and under flow tracking the tab idle hover and the related-tests summary row | **RUN** (§G, thirty-seven states on six page columns) |
| The scenario "#" link, the copy-name button, the parameterized-test table rows and the failure-cluster link are blue on a violet page and are not toolbar controls | **RUN** (§G) |
| With the width rules but no band wrap, the CI-run report still scrolls sideways 770 to 920 px and the cluster escapes through 930 | **RUN** (§C, the variant) |
| A classic 15 px scrollbar moves HEAD's bands by up to 20 px (scroll to 1060 with +300, export wrap to 1280, cluster to 1080); the prototype stays clean on every shape | **RUN** and **PROTOTYPE** (§M; Chromium without Playwright's `--hide-scrollbars`) |
| Injected `::-webkit-scrollbar` CSS does not bring the scrollbar back under `--hide-scrollbars` (`clientWidth` stays equal to the window) | **RUN** (§M) |
| Narrowed from 1400 px or widened from 320 px without a reload, the prototype is clean at every width; at HEAD the widened page keeps the phone script's inline `display: flex` on its toolbars and squeezes their labels at 780 to 860 px even without flow tracking | **PROTOTYPE** and **RUN** (§M, `MODE=shrink` and `MODE=grow`) |
| Firefox 148 and WebKit 26.4 show HEAD's defects with the bands within 40 px of Chromium's (scroll 780 to 1060 and 780 to 1040; full-toolbar clipping to 1200 and to 1240), and the prototype clean on every shape | **RUN** and **PROTOTYPE** (§M) |
| Under the WCAG 1.4.12 text-spacing override a 1100 px breakpoint leaves sideways scroll at 1120 to 1140 px (+10 on the ordinary CI report, +29 on the long branch, +54 with a scrollbar too), because the in-row box (163 to 183 px) is narrower than one letter-spaced export button (185 px); at 1160 px it is clean, with and without a scrollbar, except one width (1170) where a long branch with both stresses edges the cluster past the box without scrolling the page | **PROTOTYPE** (§O) |
| Under the text-spacing override the run summary table overflows 19 px at 320 px (34 px at 320 to 340 with a scrollbar), at HEAD and after the prototype alike; it is not a toolbar element | **RUN** (§O, §Q) |
| Firefox gives a 769 px viewport a width of 768.8 CSS px at 125 % and 768.97 at 175 %, matching neither `max-width: 768px` nor `min-width: 769px`; there the prototype with a 769 lower bound scrolls sideways by 158 px (431 on the long branch) and with `768.02px` is clean | **RUN** and **PROTOTYPE** (§R, `fractional.js`) |
| Chromium produces fractional widths too (961.6 px for a 961 px window at 125 %), but Playwright's Chromium viewport is whole CSS pixels, so the E2E lane cannot reach the gap | **RUN** (§R) |
| The prototype clears sideways scroll, in-button wrap (export, top bar, scenario toolbar), cluster escape and clipped toolbar controls at every width in all nine synthetic shapes and on the three published reports, with the header wrapping from 769 to the breakpoint | **PROTOTYPE** (§D, §F, §L, §N, and the final design in §Q) |
| The prototype paints violet for every toolbar state in §G on both violet shapes, and leaves every blue-report probe unchanged except the tabs F5 styles | **PROTOTYPE** and **PROTOTYPE (C#)** (§G; the C# build's census matches the string surgery's on every row but one, a hover read mid-transition) |
| S3 written as "the base sheet alone" removes one newline from every report without a custom sheet; written `HtmlReportStyleSheet + "\n"` it leaves those reports byte-identical | **PROTOTYPE (C#)** (§S, `bytediff.py`) |
| S5 composed at the two call sites puts the popup sheet after the theme and before `CustomCss` in `Specifications.html`, after the component sheets and before `CustomCss` in `TestRunReport.html`, and nowhere without flow tracking; `GenerateHtmlReport` keeps its signature | **PROTOTYPE (C#)** (§S, `s5.cs` through `CreateStandardReportsWithDiagrams`) |
| The C# prototype passes the unit suite (5,482 passed, 1 skipped, 0 failed) and the E2E suite (778 passed, 0 failed; the three doc-asset generators excluded as CI excludes them) | **PROTOTYPE (C#)** (§S) |
| The exported filtered HTML carries the new rules and the new sheet order, and sweeps clean | **PROTOTYPE** (§S; exported through the page's own button) |
| At 1400 px the scenario toolbar floats on the title line, 24 px tall, in both states; the flex state has 16 px of side padding the block state lacks, so its controls sit 16 px further in | **RUN** (§E, §H) and **READ** (`ReportGenerator.cs:884`: the spacer is an inline `<span>`, so `flex: 1` acts only in a flex row) |
| Stacked at 800 to 880 px the prototype wraps the toolbar to two 24 px rows with no label wrapped, the control after the spacer on the second row at the left, which is where the block toolbar puts it today | **PROTOTYPE** (§H, screenshots) |
| The moved rules and the two ≤ 768 px blocks share no property, so the source-order flip S2 causes changes nothing at 768 px and below | **READ** (`internal-flow-popup-styles.css:114-127` against `stylesheets.css:1774-1781` and `collapsible-notes-styles.css:36-41`) |
| `.collapse-all-notes-btn` and `.toggle-headers-btn` in the popup sheet have no emitter | **RUN** (`grep` over `src/Kronikol` `.cs` and `.js`: only the stylesheet matches) |
| `InternalFlowPopupCustomStyleSheet` is read by nothing | **RUN** (`grep` over `src`: the declaration is the only match) and **READ** (`../Kronikol4J/docs/REMAINING_PARITY.md:1575` says the same) |
| The merge path renders with `stylesheet: null`, so merged reports never carry the theme; it renders the run report only | **READ** (`MergeableReportRenderer.cs:65-70`, `includeTestRunData: true`) |
| `GenerateHtmlReport` has three callers: the two report call sites (`ReportGenerator.cs:422`, `:427`) and the merge renderer; a fourth is a benchmark probe under `tools/` | **RUN** (`grep`) |
| The existing tests spot-check 375, 520, 1400 and 1920 px, and none of them notices any defect in this plan (the whole suite passes on HEAD and on the prototype alike) | **READ** (`MobileResponsiveTests.cs`, `DiagramToggleLayoutTests.cs:28-72`, `PlaywrightTestBase.cs:21-22`) and **PROTOTYPE (C#)** |
| `report-init-script.js` decides "mobile" once, at load, with `matchMedia('(max-width: 768px)')` | **READ** (`:25`, and `MobileResponsiveTests.cs:149-168` already reloads after a resize for that reason) |
| The E2E lane runs on `ubuntu-latest`, Chromium headless; CI's Remainder group excludes by name every class a named group runs | **READ** (`ci.yml:47`, `:91-93`, `:119-122`, `PlaywrightFixture.cs:20-22`) |
| `overflow-wrap: anywhere` (Chrome 80, Firefox 65, Safari 15.4) sits inside the report's own floor, set by `DecompressionStream` (Chrome 80, Firefox 113, Safari 16.4); the range syntax `(768px < width <= 1160px)` would not (Chrome 104) | **READ** (MDN compatibility data; `PlantUML-Browser-Rendering.md:66`) |
| Three of the five Java stylesheet copies already differ from .NET's | **RUN** (`diff`: `stylesheets.css` lacks the deep-search chip and `.step-background` hunks, `collapsible-notes-styles.css` the note-select hunk, `inline-svg-styles.css` the fragment-placeholder hunk; `internal-flow-popup-styles.css` and `context-menu-styles.css` identical) |
| The Java renderer appends the custom sheet at the same place, by design, and its comment gives the composition as `HtmlReportStyleSheet + "\n" + (stylesheet ?? "")` | **READ** (`DotNetHtmlReportRenderer.java:320-325`) |
| The Java port has no violet constant of its own | **RUN** (`grep DDD6FE` over the Java sources matches nothing) |
| Not run: Safari on macOS or iOS (WebKit 26.4 on Windows is the proxy); a phone's own browser; text-only zoom; the Server, Local and InlineSvg renderings (their toolbars carry only the whole-test-flow toggles); the E2E guard itself, which is what §5 builds | |

---

## 2. The toolbar today (measured at HEAD)

### 2.1 The fixture

`gen.cs` writes pages from one data set: two features, six scenarios (passed, failed, skipped,
with durations from 0.1 to 9 s), six tags, no categories, three diagrams whose source declares one
actor and **seventeen participants** with the length distribution the 2026-08-31 investigation
measured on the published report (names 9 to 31 characters, 256 label characters in total, median
13; the names are synthetic), and a `database` participant so the Databases toggle exists. The
shapes:

| Page | Stylesheet | Run data | CI metadata | Flow tracking | Stands for |
|---|---|---|---|---|---|
| `TestRunReport.html` | none | yes | GitHub Actions, branch `main` | no | the report every CI run writes |
| `TestRunReport_noci.html` | none | yes | none | no | the E2E fixture's shape, and a local run |
| `TestRunReport_iflow.html` | none | yes | GitHub Actions | yes | a report with the popup sheet |
| `Specifications.html` | violet | no | none | no | the default `Specifications.html` |
| `Specifications_iflow.html` | violet | no | none | yes | the same with the popup sheet |
| `TestRunReport_longci.html` (`-- stress`) | none | yes | 69-character branch, 52-character repository | no | a feature branch on a named repository (§2.6) |
| `TestRunReport_full.html` (`-- full`) | none | yes | GitHub Actions | yes, with whole-test flow views | every toolbar control: the Sequence, Activity and Flame tabs, Assertions (an `<<assertionNote>>`), Steps (a `<<stepDelimiter>>`), Databases, the opt-in note font select, and the default component diagram in the top bar (§2.9, §2.10) |
| `TestRunReport_full_longci.html` (`-- full`) | none | yes | the long strings | yes | both at once: the guard's fixture (§4) |
| `Specifications_full.html` (`-- full`) | violet | no | none | yes | the full toolbar on the violet page |

Rendering is `BrowserJs`, so `collapsible-notes-styles.css` is in every page and
`internal-flow-popup-styles.css` only in the flow pages, which is the F5 split. The three published
reports (§2.13) are the real counterpart: 158 scenario toolbars of up to ten buttons each.

### 2.2 The width sweep

Every width from 320 to 1400 in 10 px steps, reloaded at each (the init script decides the mobile
layout at load), every `<details>` opened and every phone-hidden toolbar shown, measured before any
diagram renders (the SVG is `max-width: 100%` above 768 px and inside an `overflow-x: auto` container
below, so it is not a width source; the 2026-08-31 mock found the same). A wrapped label is a button
whose text range has more than one client rect.

| Shape | Sideways scroll | Peak overflow | Export labels wrap | Cluster outside the box | Box width at 770 / 900 / 1000 / 1100 / 1200 |
|---|---|---|---|---|---|
| TestRunReport, CI | **770 to 1060** | +295 at 770 | **320 to 380 and 770 to 1260** | 770 to 1070 | 32 / 125 / 225 / 325 / 425 |
| TestRunReport, no CI | 770 to 900 | +141 at 770 | 320 to 380 and 770 to 1110 | 770 to 910 | 149 / 279 / 379 / 479 / 579 |
| TestRunReport, CI, flow | as the CI row | | | | |
| TestRunReport, long CI strings | **780 to 1320** | +558 | 320 to 380 and 780 to 1400 | 780 to 1340 | 32 at 900 and 1000, 152 at 1200 |
| Specifications, violet | none | | 320 to 380 only | never | full width (754 at 770) |
| Specifications, flow | none | | 320 to 380 only | never | full width |

The overflowing element at every overflowing width is the export cluster. Without a component diagram
the top bar (`.toolbar-row`) wraps no label inside a button at any width; with one it does (§2.10). At
768 px and below the column layout holds everywhere, long branch included: in a column the CI box is
as wide as the column and its cells wrap at hyphens. The investigation's figures (labels wrapping
"from a 1200 px window down", "1090 px of scroll width in a 900 px window") were taken on the published
report, whose summary table and CI box differ from the fixture's by tens of pixels; the bands agree in
shape and cause, and §2.13 measures the published report itself.

Where the numbers come from (READ): `.header-row { display: flex; gap: 1em }` (`stylesheets.css:104`)
holds the summary table, the CI-plus-donut group (`.ci-chart-group`, `flex-shrink: 0`, `:117`) and
the filtering box (`flex: 1; min-width: 0`, `:137`), with no wrap and no breakpoint between 769 px
and full width. The box takes whatever is left, which with CI metadata is 32 px at 770. Inside it
`.filtering-box-header` (`:150`) is a non-wrapping flex row of the `Filtering` heading and the
`.filtering-box-export` cluster (`:157`, also non-wrapping), whose three buttons (`.export-btn`,
`:581`) may shrink to their longest word, so the labels wrap to two and three lines; when even that
does not fit, the cluster runs out of the box and the page scrolls. The search input is not part of
the problem: `#searchbar` (`:176`) is a flex item of `.filter-search` (`:168`) with `width: 100%`,
which caps its automatic minimum, so it shrinks with the box (15 px wide at 780 px on the CI shape)
and holds nothing open; the first pass's "intrinsic width, about 184 px, is a floor" was a reading,
and the grouping's "min-width on the search input" is answered by measurement (§K). The CI box
(`.ci-metadata`, `:126`, `flex-shrink: 0`) is a table whose cells never wrap, so its width is the
longest of the branch, repository and pipeline strings (§2.6).

### 2.3 The scenario toolbar without flow tracking (F5, F11)

`ReportGenerator.cs:1180` includes `internal-flow-popup-styles.css` only when `internalFlowTracking`
is true, and that sheet holds the six rules that make `.diagram-toggle` a flex row with styled
buttons (`:114-127`). The toolbar itself is emitted at six sites for every `BrowserJs` report
(`:1951`, `:1967`, `:1980`, `:2998`, `:3029`, `:3037`), so in every report without flow tracking
it is a `display: block` div. Measured (harness §B): `display: block`, and a button carrying
`diagram-toggle-btn diagram-toggle-active` paints the user agent's default `rgb(240, 240, 240)` with
no hover change; with the sheet present the same button is `rgb(66, 133, 244)`, `rgb(51, 103, 214)`
on hover, idle `rgb(245, 245, 245)`, idle hover `rgb(232, 240, 254)`.

The visible difference on a wide window (harness §E, §H): at 1400 px the toolbar floats on the title
line (`diagram-toggle-layout-script.js`) at 24 px tall in both states, because the spacer is an
inline `<span>` (`:884`) and a floated flex row shrink-wraps; the flex state carries 16 px of side
padding (`padding-left: 1em; padding-right: 1em` in the popup sheet) that the block state lacks, so
its controls run from 543 to 1327 px where the block's run from 559 to 1343, the toolbar's right edge
at 1343 in both. In the stacked state (a narrow window, or a scenario with many toggles) the block
toolbar wraps like inline text, the last control dropping to a second line at the left (850 and
800 px, 49 px tall); the flex toolbar, with no `flex-wrap` above 768 px (`stylesheets.css:1774` wraps
it only at ≤ 768), squeezes instead: at 800, 850 and 880 px three labels wrap inside their buttons
("Headers Shown", "Databases Shown" on two lines, 39 px tall). That is F11 on the fixture's five
controls; §2.9 is F11 on a full toolbar, where squeezing is not enough.

### 2.4 The theme cascade

Colours are computed `background-color` / `border-top-color` at 1400 px, thirty-seven control states
on each of six page columns (harness §G, `cascade2.js`; the states are synthetic elements carrying
the real classes, appended to the page, so a class paints as it paints on emitted markup, and the
table also records which controls the default `Specifications.html` shows). The violet theme intends
`rgb(139, 92, 246)` (`#8B5CF6`) for an active control, `rgb(124, 58, 237)` (`#7C3AED`) for its hover
and `rgb(237, 233, 254)` (`#EDE9FE`) with a `#A78BFA` border for an idle hover; the base sheets use
`rgb(66, 133, 244)` (`#4285f4`), `rgb(51, 103, 214)` or `rgb(50, 110, 220)`, and `rgb(230, 240, 255)`
with `rgb(100, 150, 255)`. On the violet pages, at HEAD and after the prototype:

| Control on a violet page | At HEAD | After S3 + S4 | On the default `Specifications.html` |
|---|---|---|---|
| Filter toggle (happy path, dependency, status, category), active, hovered | **pale tint: loses its active colour** | violet | yes |
| Dependency and category mode toggle (AND / OR), hovered | **blue tint, blue border** | violet | yes |
| Percentile button, idle, hovered | **blue** | violet | yes |
| Percentile button, active | violet with a **blue border** | violet | after a click |
| Collapse / expand all, hovered | **blue** | violet | yes |
| Timeline toggle: idle hover / active / active hover | **blue / blue / dark blue** | violet / violet / dark violet | yes (idle) |
| Export button, hovered | **blue** | violet | yes |
| Details radio: idle hover / active / active hover | **blue / blue / blue** | violet | yes |
| Diagram tab, no flow tracking: idle / idle hover / active / active hover | **unstyled / unstyled** / violet / violet | grey / violet tint / violet / dark violet | with the Activity tab |
| Diagram tab, flow tracking: idle hover / active / active hover | **blue tint / blue / dark blue** | violet tint / violet / dark violet | with the Activity tab |
| "Diagram Settings" button (phone), hovered | **blue** | violet | at ≤ 768 px |
| Flow toggle, flow tracking: active / active hover | **blue / dark blue** | violet / dark violet | in the popup |
| Related-tests list item, flow tracking, hovered | **blue tint** | violet tint | in the popup |
| Related-tests summary row, flow tracking, hovered | **blue tint** | violet tint | in the popup |
| Scenario "#" link and copy-name button, hovered | blue tint | blue tint (Q3) | yes |
| Parameterized-test table row: hovered / active | blue tints | blue tints (Q3) | with examples |
| Failure-cluster link | blue | blue (run reports only, never themed by default) | no |

Every blue-report probe reads the same before and after, except the tabs F5 styles. The first row is
worst on a phone: a tap leaves the control hovered, so on an emulated phone (390 × 844, touch) the
"Happy Paths Only" filter just tapped is active and paints white text on `rgb(237, 233, 254)`, the
label close to invisible and the control looking unselected, until the next tap lands elsewhere; the
prototype paints it `rgb(139, 92, 246)` (harness §P, `tap-*` screenshots). The causes (READ):

- `combinedStylesheet` is the base sheet followed by the custom sheet (`ReportGenerator.cs:1099-1102`),
  and the `<style>` block (`:1201-1207`) then appends the context-menu, inline-SVG, collapsible-notes
  and internal-flow sheets, with `CustomCss` in its own block after all of them (`:1186`). So
  `.details-radio-btn.details-active` in the violet constant (`Stylesheets.cs:61`, specificity 0,2,0)
  is followed by the same selector in `collapsible-notes-styles.css:12` (0,2,0): the later one wins,
  in every `BrowserJs` report, on the default configuration of `Specifications.html`. Likewise
  `.diagram-toggle-active` and `.iflow-toggle-active` (`:69`, `:67`, 0,1,0) lose to the popup sheet's
  `:125` and `:72`, and `.iflow-rel-list li:hover` (`:70`) to its `:144`, whenever flow tracking is on.
- `.diagram-toggle-active:hover` and `.iflow-toggle-active:hover` in the popup sheet (`:126`, `:73`,
  0,2,0) beat a theme's plain active rule (0,1,0) whatever the order. The violet constant has the
  flow-toggle hover (`:68`) but not the tab hover: that is F6 as recorded.
- Inside the constant, the hover rules for the four filter toggles (`:47-53`, 0,2,0) follow their
  active rules (`:34-41`, 0,2,0), so on hover the pale tint wins over the active colour. The base
  sheet has them the other way round (`stylesheets.css:373` before `:378`), which is why the blue
  report keeps its active colour on hover.
- The constant overrides no hover for `.export-btn`, `.details-radio-btn`, `.collapse-expand-all`,
  `.percentile-btn`, `.timeline-toggle`, `.dep-mode-toggle`, `.cat-mode-toggle`,
  `.scenario-diagram-controls-toggle`, `.diagram-toggle-btn` or `.iflow-toggle-btn`, no active rule
  for `.timeline-toggle-active`, no border for `.percentile-btn.percentile-active` (`:42-45` sets
  background and colour only) and nothing for `.iflow-rel-summary-table tr:hover td`. The base sheets
  tint all of them blue (`stylesheets.css:373-1673` and the two component sheets; the harness README
  lists every line).

The investigation's F6 ("Violet overrides `.diagram-toggle-active` (0-1-0) not `:hover` (0-2-0), so
a violet active tab flashes blue on hover") was a reading of specificity; the measurement says the
tab is blue at rest as well under flow tracking, and violet with no hover change without it. The
memory file was corrected with this plan.

### 2.5 What the fixes do to the same sweep

The final design (§3: the band from `768.02px` to **1160 px**, no search-input rule, the
`.toolbar-left` wrap) applied to every shape, Chromium, reloaded at every width in 10 px steps
(harness §Q; the first pass's 1100 px prototype read the same apart from the band edge):

| Shape | Sideways scroll | Label wrap (export / top bar / scenario) | Cluster outside the box | Toolbar controls clipped | Header at 900 | Box width at 900 / 1000 / 1100 / 1200 |
|---|---|---|---|---|---|---|
| all nine synthetic shapes | none at any width | never / never / never | never | never | wraps, box on its own row | 884 / 984 / 1084 / in-row (425 with CI, 579 without, 347 with the long CI strings, full width on Specifications) |
| the three published reports | none | never / never / never | never | never | wraps | 884 / 984 / 1084 / in-row |

Cascade after the prototype: every row of the §2.4 table reads violet on both violet shapes, in all
three states where it has three; every blue-report probe reads what it read before.

**The variant D5 rests on** (`patch.js 0`, the width rules and the toolbar rules with no band wrap):
the CI-run report still scrolls sideways from 770 to 920 px (+160 at 770), with the cluster outside
the box through 930, and the no-CI report by 6 px at 770 (the feature summary table). So the box rules
are not enough on their own: the band wrap is what removes the scroll on the shape every CI run
produces, and above 920 px it is what makes a box of 125 to 325 px usable at all. Where the band ends
is §10 Q1; §2.11 measures the edge under the conditions that move it.

### 2.6 Long CI strings (what the fixture held fixed)

The branch and repository names in the fixture are `main` and `example/BreakfastProvider`. A feature
branch is not: `gen.cs -- stress` writes the same report with the branch
`feature/KRON-1234-reconcile-nightly-settlement-batches-across-regions` (69 characters) and the
repository `my-organisation/breakfast-provider-integration-tests` (52). Measured (harness §F):

| Shape, rules applied | Sideways scroll | Cluster outside the box | Box at 900 / 1000 / 1200 | CI box at 1400 |
|---|---|---|---|---|
| Long CI, HEAD | **780 to 1320**, +558 at peak | 780 to 1340 | 32 / 32 / 152 | 547 |
| Long CI, S1 + S2 (the first prototype) | **1120 to 1200**, +83 | 1120 to 1200 | 884 / 984 / 152 | 547 |
| Long CI, plus `.ci-metadata td { overflow-wrap: anywhere }` alone | 1120 to 1200 | 1120 to 1200 | as above | 547 (a `flex-shrink: 0` box is never squeezed, so the cells never need to break) |
| Long CI, plus S1b (the cap) | none | never | 884 / 984 / 347 | **352** |
| Short CI (the ordinary fixture), plus S1b | none | never | 884 / 984 / 425 | 274, unchanged |

So above the band the row is summary plus CI box plus whatever is left, and a 547 px CI box leaves
152 px at 1200: the cluster escapes again. The cap makes the CI box wrap its values instead. A first
cap let the label cells break too ("Branch" / ":" on two lines); the label cells are kept whole and
only the value cells break (S1b). At 768 px and below the cap is not applied: the column layout
already bounds the box, and the 600 px screenshot is unchanged.

### 2.7 A wider font

The E2E lane runs on `ubuntu-latest` (`ci.yml:47`), where `sans-serif` is DejaVu Sans, wider than the
Arial the harness measured with on Windows; and buttons do not inherit the body font, so a font
injected on `body` alone stresses the summary and CI tables but not the labels. The prototype swept
under `body, button, input, select { font-family: Verdana }`, wider than DejaVu Sans, is clean at
every width on every shape (harness §I, and §Q for the final design): the CI box grows from 274 to
306 px, the in-row box at 1200 shrinks from 425 to 340, the widest export button at 320 px is 142 px in
a 256 px box. The assertions are shape assertions, so a font only moves the bands; the one
width-critical case, a single export button wider than the box at 320 px, has 114 px of slack.

### 2.8 What it looks like (screenshots in the harness `shots/` folder)

- HEAD at 900 px: the box is a 125 px sliver on the right, the export buttons cut off at the edge,
  "Happy Paths Only" and "Azure Event Hub" on two lines each.
- Prototype at 770, 900 and 1050 px: summary and CI-plus-donut side by side, the box on its own full
  row beneath; about 150 px of empty band to the right of the CI group at 770 and 900, about 300 px
  at 1050. With the breakpoint at 1160 the same layout holds through 1160: about 390 px of empty band
  at 1150 (`out-bp1160-TestRunReport-1150`). That void is D5's cost (Q1).
- Prototype at the first row-layout width: at 1161 px with the 1160 breakpoint the box is back in the
  row, about 370 px wide, the CSV button on the cluster's second row, chips two to four per line
  (`out-bp1160-TestRunReport-1161`); at 1101 px with a 1100 breakpoint it was 245 px, the export
  buttons one per row.
- Prototype at 320 px: "Clear All" and "Export Filtered HTML" on the first row of the cluster, the
  CSV button on the second.
- Long CI at 1200 px, HEAD: the CI box 547 px wide, the branch on one line, the box a sliver with
  "Clear All" broken across two lines and outside it. With S1b: the branch on two lines, broken at a
  hyphen, the label column whole, the box 347 px wide and clean.
- The scenario toolbar stacked at 850 px: HEAD with flow tracking, "Headers Shown" and "Databases
  Shown" wrapped inside their buttons; the prototype, every label whole, the width select on a second
  row at the left, which is where the block toolbar (HEAD without flow tracking) puts it too.
- The full toolbar at HEAD, 780 and 1000 px (`clip-out-full-TestRunReport_full-*`): at 780 nothing
  after "Headers Shown" is visible; at 1000 "Databases Shown" is cut through its middle and the three
  selects are gone; every label on two lines. The prototype at 780 px: three rows, every control
  visible, every label whole.
- A phone at HEAD (`tap-out-head-happy-path-sticky`): the tapped "Happy Paths Only" filter as white text
  on pale lavender.

### 2.9 The full toolbar is clipped, not squeezed (F11, measured again)

The fixture's toolbar had five controls. A report can give a scenario toolbar up to fourteen: the
Sequence, Activity and Flame tabs when the whole-test flow views exist, the Details radio (three
buttons, a select and a label), Headers, Assertions when a diagram carries an `<<assertionNote>>`,
Steps when it carries a `<<stepDelimiter>>`, Databases when it declares a `database` participant
(`ReportGenerator.cs:1107-1115`), and the note format, note font (opt-in) and note width selects.
`gen.cs -- full` writes that toolbar (§2.1). At HEAD, under flow tracking, it is a flex row that does
not wrap, its controls cannot shrink below their longest word (the selects not at all), and the row
runs past the toolbar's own right edge (harness §L):

| Width | Controls past the toolbar's edge | Out of sight |
|---|---|---|
| 780 | 6, the last ending at x = 1244 | Assertions, Steps, Databases, the three selects |
| 1000 | 4 | half of Databases, the three selects |
| 1200 | 1 | the width select |
| 1300 and up | 0 | none; the labels still wrap inside their buttons up to 1400 |

They are out of sight rather than scrolled to, because `.feature` and `.scenario` carry
`content-visibility: auto` with an intrinsic-size hint (`stylesheets.css:28`, `:38`): the property
applies paint containment, so everything that overflows the scenario is clipped at its edge, and none
of it reaches the document's scroll width (the page reads 1065 px at 780, the export cluster's
overflow, while the last control ends at 1244). The first pass's sweep asserted on the scroll width
and could not have seen it; neither can the guard's first assertion (§4 adds one that can). Firefox
clips the same toolbar to 1200 px and WebKit to 1240 (§M). With a classic scrollbar even the fixture's
five-control toolbar clips a control at 780 to 800 px.

On the published reports (§2.13) this is the defect a reader meets: 158 toolbars of up to ten
buttons, controls clipped from 780 to 1300 px on the run report and on `Specifications.html`, up to
522 px of them. S2's `flex-wrap: wrap` turns the row into rows (three at 780 px on the full toolbar,
two at 1000), every control visible and every label whole, on every shape and in every engine
measured. So Q2's line of F11 is not only the precondition item 3 says; it is the fix for the worse
half of F11.

### 2.10 The top bar with the default component diagram

`ComponentDiagramOptions.EmbedInTestRunReport` defaults to true (`ComponentDiagram/ComponentDiagramOptions.cs:14`),
and whenever a run has tracked dependencies the run report's top bar carries a fourth button,
"Component Diagram", in `.toolbar-left` (`ReportGenerator.cs:1503-1513`) beside "Expand All Features",
"Expand All Scenarios" and "Scenario Timeline". `.toolbar-left` is a flex row (`stylesheets.css:605`)
that gets `flex-wrap` only in the ≤ 480 px block (`:1890`), and at ≤ 768 px the top bar is a column
(`:1754`) in which the group is as wide as the window allows. From 500 to 580 px the four buttons do
not fit one line, the row does not wrap, and all four labels wrap inside their buttons (121, 127, 112
and 127 px wide at 540 px, two lines each), at every measured width from 500 to 580 px, at HEAD and on
the published run reports (Firefox 500 to 560, WebKit 500 to 540); at 480 and below the ≤ 480 block
wraps the group, and from 600 up the four fit one line. The first pass's fixture called
`GenerateHtmlReport` without a component diagram, so its top bar had three buttons and never wrapped:
the guard as first specified would have passed over this while asserting that no top-bar label wraps.
One declaration, `.toolbar-left { flex-wrap: wrap }`, makes the four wrap as whole buttons; measured
clean on every shape (S1).

### 2.11 The conditions the first sweep held fixed

The first sweep ran Chromium with Playwright's defaults: scrollbars hidden (`--hide-scrollbars`), the
page reloaded at every width, the author's default fonts and spacing. Each of those was varied on
HEAD and on the prototype (harness §M and §O, `sweep2.js`; 20 px steps):

| Condition | HEAD | Prototype |
|---|---|---|
| Classic scrollbar (15 px, Chromium without `--hide-scrollbars`) | the bands move by up to 20 px: scroll 780 to 1060 (+300), export wrap to 1280, cluster to 1080; the five-control toolbar clips at 780 to 800 | clean on every shape |
| No reload, narrowed from 1400 (a window snapped to half a screen) | the reload bands | clean on every shape |
| No reload, widened from 320 (a phone or small tablet rotated) | the reload bands, plus: the phone script's inline `display: flex` stays on every toolbar, so even without flow tracking the labels wrap at 780 to 860 | clean on every shape; the "Diagram Settings" buttons the phone script inserted stay visible, which is the script's once-at-load decision and not a width defect |
| Firefox 148 | scroll 780 to 1060 (+282), export wrap 320 to 360 and 780 to 1240, full-toolbar clipping 780 to 1200 | clean on every shape |
| WebKit 26.4 | scroll 780 to 1040 (+276), full-toolbar clipping 780 to 1240 | clean on every shape |
| Verdana on body and controls | (not re-run) | clean on every shape |
| WCAG 1.4.12 text spacing (line height 1.5, letter spacing 0.12 em, word spacing 0.16 em) | the §2.2 bands and more | with a 1160 px breakpoint clean on every shape but one width (1170, long branch, with a scrollbar too: the cluster edges past the box, the page does not scroll); with a 1100 px breakpoint the page scrolls sideways at 1120 to 1140 px (+10 on the CI report, +29 on the long branch, +54 with a scrollbar); at 320 px the run summary table overflows 19 px on both (34 px, to 340, with a scrollbar), HEAD too |

The scrollbar matters for the guard: Playwright's Chromium hides it by launch argument, injected
`::-webkit-scrollbar` CSS does not bring it back (`clientWidth` stays the window's width), and a
desktop Chrome on Windows or Linux draws it, taking 15 px of layout while the media queries still see
the full window. The guard therefore launches its own Chromium without that argument (§4). The text
spacing result is what moves D5: at 1101 to 1160 px the in-row box is 163 to 183 px under the
override, narrower than one letter-spaced "Export Filtered HTML" (185 px), and at 1160 the first
in-row width has room (§10 Q1).

### 2.12 The gap between 768 and 769 px

The existing sheets hold only `max-width` queries (768 px in four sheets and the violet constant, 480
in the base sheet). S1 and S1b as first written added the first `min-width` queries, `769px`, and
with them a gap: a viewport wider than 768 and narrower than 769 CSS px matches neither. Browsers
produce such widths at non-integer scale factors. Measured (harness §R, `fractional.js`):

| Browser, scale | Viewport given | CSS width | Phone block | `min-width: 769px` | Prototype with 769 | Prototype with 768.02 |
|---|---|---|---|---|---|---|
| Firefox, 125 % | 769 | 768.8 | no | no | header in one row, **+158 px** (+431 long branch) | band, clean |
| Firefox, 175 % | 769 | 768.97 | no | no | **+158 px** (+431) | band, clean |
| Firefox, 150 % / 110 % | 769 | 769.33 / 769.08 | no | yes | band, clean | band, clean |
| Chromium, 125 % | window 961 | 961.6 | | | | |

Chromium makes fractional widths too, but Playwright's Chromium viewport is whole CSS pixels, so the
E2E lane cannot land in the gap; T1 pins the bound instead. Writing the lower bound `768.02px`
closes it: between 768 and 768.02 no device-pixel width maps to a CSS width at any scale factor below
50. The `0.02` rather than `0.01` follows the Bootstrap convention, which cites a WebKit rounding bug
at two decimal places. The range syntax `(768px < width <= 1160px)` would say it more plainly but
needs Chrome 104, beyond the report's own floor (§1).

### 2.13 The published reports

The xUnit and LightBDD run reports and the xUnit `Specifications.html` BreakfastProvider publishes
(3.29.0, 8 to 9 MB each, real names, real toolbars: flow tracking with the whole-test views,
assertion notes, step bars, database participants, the default component diagram), Chromium, 20 px
steps (harness §N):

| Page | Sideways scroll | Export wrap | Top-bar wrap | Scenario labels wrap | Toolbar controls clipped | Box at 900 / 1000 / 1100 / 1200 |
|---|---|---|---|---|---|---|
| xUnit run report, HEAD | 780 to 1080, +310 | 320 to 380, 780 to 1280 | 500 to 580 | 780 to 1400 | **780 to 1300, up to 522 px** | 100 / 200 / 300 / 400 |
| xUnit `Specifications.html`, HEAD | none | 320 to 380 | never | 780 to 1400 | **780 to 1300** | full width |
| LightBDD run report, HEAD | as the xUnit run report | | | | | |
| each of the three, prototype | none | never | never | never | never | 884 / 984 / 1084 / in-row |

What the prototype leaves clipped on these pages is not the toolbar. The parameterized-test table is
`display: table` above 768 px and is the scenario's direct child in the grouped view, so a wide one
runs past the scenario and is clipped (783 px in a 720 px content box at 800 px, up to 81 px at 780
to 840, and to 860 with a classic scrollbar); at 768 px and below it becomes a scroll container (`stylesheets.css:1807`) and is fine, but
on the LightBDD report the row detail panels under it (`.param-detail-panels`, `:1611`) hold a
sub-table 757 px wide in a 336 px scenario at 400 px, clipped from 320 to 760 px. Same mechanism as
§2.9, different element: Q7.

### 2.14 The C# prototype

The §3 change was made in the real files (`stylesheets.css`, `internal-flow-popup-styles.css`,
`Constants/Stylesheets.cs`, `ReportGenerator.cs`) in a detached worktree at `753b8790`, built, and
measured (harness §S; the diff is `prototype.diff`):

- **S3's bytes.** Written as the plan first worded it, `combinedStylesheet` becoming the base sheet
  alone, every report without a custom sheet lost one byte: the `{stylesheet}` line of the raw
  string produced a newline even when the sheet was null. Written `HtmlReportStyleSheet + "\n"`, the
  three no-sheet shapes are byte-identical to HEAD's output (0 changed lines outside the clock), and
  `Specifications.html` is a reorder plus one byte. The Java renderer's comment
  (`DotNetHtmlReportRenderer.java:320`) states the same composition.
- **S5's order**, driven through `CreateStandardReportsWithDiagrams` with marker sheets
  (`s5.cs`): in `Specifications.html` the notes sheet, then the popup sheet, then the theme, then
  the popup custom sheet, then `CustomCss`; in `TestRunReport.html` the popup sheet, then the popup
  custom sheet, then `CustomCss`; with flow tracking off the popup custom sheet is absent from both.
- **Layout and paint.** The nine shapes regenerated through the prototype sweep clean at every width
  with the same box, CI-box and search-input widths as the string surgery; the census matches the
  string surgery's on every row but one (the parameterized-row hover, read mid-transition at 0.345
  and 0.114 alpha on the two runs).
- **The suites.** `Kronikol.Tests`: 5,482 passed, 1 skipped (the 100 MB streaming test, skipped on
  HEAD too), 0 failed. `Kronikol.Tests.EndToEnd` without the three doc-asset generators CI also
  excludes: 778 passed, 0 failed, 6 min 42 s. No stored output pins the stylesheet, and no existing
  test notices any defect this plan fixes.
- **The export.** "Export Filtered HTML" on a prototype page wrote a file carrying the new rules and
  the new sheet order, and that file sweeps clean.

A trap for whoever repeats this: a fresh worktree on a `core.autocrlf=true` machine checks the `.js`
resources out with CRLF, and `plantuml-worker-host.js` is embedded raw, so a byte diff across two
checkouts differs in `WORKER_HOST_SOURCE`. §3.3's diff compares two builds from one checkout;
`bytediff.py` normalises the escape when it cannot.

---

## 3. Design

### 3.1 S1: the widths (`stylesheets.css`)

Four declarations added to existing rules, and one media block:

```css
.filtering-box-header { /* :150 */ flex-wrap: wrap; }
.filtering-box-export { /* :157 */ flex-wrap: wrap; }
.export-btn           { /* :581 */ white-space: nowrap; }
.toolbar-left         { /* :605 */ flex-wrap: wrap; }

/* Between the phone layout and the full-width header the filtering box takes its own row:
   with the summary table and the CI box in front of it the box is 32 px wide at 770 and
   225 px at 1000 (measured, TOOLBAR_AT_EVERY_WIDTH_PLAN.md §2.2). 768.02, not 769: a
   fractional viewport between 768 and 769 px (Firefox at 125 %) must not fall between this
   block and the phone block (§2.12). The ≥ 1351 px band grid is TOOLBAR_REDESIGN_PLAN.md §3;
   this is the minimal wrap on today's markup. */
@media (min-width: 768.02px) and (max-width: 1160px) {
    .header-row { flex-wrap: wrap; }
    .filtering-box { flex-basis: 100%; }
}
```

The media block goes just above the `/* ── Mobile / Responsive ── */` comment (`:1697`), so the
≤ 768 block still follows it in source order, and its lower bound keeps the two ranges disjoint: the
≤ 768 block sets `flex-direction: column`, under which a `flex-basis: 100%` would size the box
against an indefinite height, and the sweep must never see the two blocks combined. `1160` is D5's
number (§10 Q1); the breakpoint appears once. `.filtering-box-header` is already a column at ≤ 768
(`:1855`), where the wrap is inert; `.toolbar-left` already wraps at ≤ 480 (`:1890`), where the new
declaration repeats it. The ≤ 480 touch-target rule (`:1919`) already lists `.export-btn`; with the
labels unwrappable the three buttons wrap as whole units, two on the first row and one on the second
at 320 px, which is what the prototype measured.

Why each: `nowrap` stops the wrap inside the button but makes the cluster wider, so the cluster
wraps as a row (`flex-wrap` on `.filtering-box-export`), and the cluster then drops under the
heading when the box cannot hold both (`flex-wrap` on `.filtering-box-header`; with
`justify-content: space-between` a lone cluster on its own row sits at the left); `.toolbar-left`
wraps its buttons whole instead of their labels (§2.10); the band wrap gives the box the full width
from 769 to 1160 px, which is the only rule that clears the 770 to 920 px scroll on a CI-run report
(§2.5). The grouping's `min-width` on the search input is not here: the input already shrinks with
the box, and the variant without it measured the same everywhere (§2.2).

### 3.1b S1b: the CI box (`stylesheets.css`, beside `.ci-metadata`, `:126`)

```css
/* The CI box is flex-shrink: 0 and its cells never wrap, so a long branch or repository name sets
   the first header row's width: a 69-character branch scrolls the page sideways from 780 to 1320 px
   without the rules above and from 1120 to 1200 px with them (measured, TOOLBAR_AT_EVERY_WIDTH_PLAN.md
   §2.6). Capped in the row layout; the column layout (≤ 768 px) bounds it already. Value cells break
   anywhere so a name without hyphens still wraps; label cells stay whole. */
.ci-metadata table { max-width: 100%; }
.ci-metadata td:first-child { white-space: nowrap; }
.ci-metadata td:last-child { overflow-wrap: anywhere; }
@media (min-width: 768.02px) {
    .ci-metadata { max-width: 20em; }
}
```

20 em is 320 px at the body size: wider than the ordinary CI box (274 px, unchanged by the cap). The
header cell (`colspan="2"`, both first and last child) gets `nowrap`, which wins, and stays on one
line. The `min-width: 768.02px` block overlaps the S1 band block in range but shares no property with
it; the `768.02` bound appears in two blocks and T1 pins both, and pins that no `769px` bound
exists. A cap by percentage was considered and rejected: the value would be tied to the row width the
band wrap already varies. `overflow-wrap: anywhere` is inside the report's browser floor (§1).

### 3.2 S2: the toolbar base moves, and wraps

The six rules at `internal-flow-popup-styles.css:114-127` (`.diagram-toggle`, `.diagram-toggle-btn`,
its `:hover`, `.diagram-toggle-active`, its `:hover`, `.diagram-toggle-spacer`) are cut from the
popup sheet and pasted into `stylesheets.css` next to the `[data-layout="inline"]` rules (`:1970`),
under the comment that already explains the toolbar there, with one addition:

```css
.diagram-toggle { …as today…; flex-wrap: wrap; }
```

Nothing else in the popup sheet refers to them (`.whole-test-flow`, `.collapse-all-notes-btn` and
`.toggle-headers-btn` stay; the last two have no emitter anywhere in `src/Kronikol`, which is dead
CSS to leave alone: the sheet is byte-shared with the port and this plan takes only what it needs).
The ≤ 768 duplicates (`stylesheets.css:1774-1781` and `collapsible-notes-styles.css:36-41`) stay as
they are: they carry the `gap`, the hidden spacer and the centred text, which are still wanted, and
collapsing them is F10, which is 6.2's. The move flips source order between the base rules and the
notes sheet's ≤ 768 block (the base sheet is emitted first, the popup sheet last), but the two share
no property (the base sets margins, padding, display, alignment, width and box-sizing on the
toolbar and padding, border, background, cursor, font size, radius and margin on the button; the
mobile blocks set `flex-wrap`, `gap`, the spacer's `display` and the button's `text-align`), so
nothing changes at 768 px and below (READ, §1). The `[data-layout="inline"]` rules (0,2,0) still beat
the base (0,1,0), so the floated toolbar keeps floating; when it does not fit beside the title the
layout script stacks it, and a stacked flex row with `flex-wrap` keeps every label on one line and
every control inside the scenario, where today's flow-tracking reports wrap the labels and, with a
full toolbar, clip the last controls out of sight (§2.3, §2.9). `DiagramToggleLayoutTests` stays
green (T9; measured on the C# prototype).

What the reader sees change (§2.3, §2.8, §2.9): in every `BrowserJs` report without flow tracking the
toolbar gains 16 px of side padding (its inline controls sit 16 px further in on the title line; its
stacked rows wrap as they do today, as a flex row instead of inline text) and styled tab buttons. In
flow-tracking reports the stacked toolbar between 769 and about 1300 px no longer squeezes its labels
into two lines or pushes its last controls out of view; the row wraps, and the controls after the
spacer land on the next row at the left instead of being held at the right edge, which is what the
≤ 768 layout does already.

### 3.3 S3: the theme after the component sheets (`ReportGenerator.cs`)

`combinedStylesheet` (`:1099-1102`) becomes the base sheet followed by the newline its second line
produced, and the custom sheet moves to the same template line as `{{internalFlowPopupStyles}}`
(`:1206`), inside the same `<style>` element and before `{{customCssBlock}}`:

```csharp
// The base sheet first; the custom sheet (the specifications theme, or a user's) goes after the
// component sheets in the <style> block below, so it wins at equal specificity. The trailing
// newline is the line the custom sheet used to occupy here: kept, so a report without one keeps
// its bytes.
var combinedStylesheet = Stylesheets.HtmlReportStyleSheet + "\n";
var themeStyles = stylesheet is null ? "" : "\n" + stylesheet;
// …
                                {{internalFlowPopupStyles}}{{themeStyles}}
```

That keeps every report with no custom sheet, which is every `TestRunReport.html` without S5's
option and every merged report (`MergeableReportRenderer.cs:65-70` passes `stylesheet: null`),
byte-identical as far as the order change goes; only `Specifications.html` and a report given a sheet
change bytes. Measured on the C# prototype (§2.14): without the `+ "\n"` every no-sheet report loses
one byte. The stylesheet rules of S1, S1b and S2 change every report's `<style>` block regardless, so
the release as a whole moves every report's bytes; what this section guarantees is that the order
change adds nothing to that. The executing session repeats the diff once, on two builds from one
checkout, and records it in §9.

This is a behaviour change, called out in the changelog: a user sheet passed as
`HtmlSpecificationsCustomStyleSheet` now wins over the component sheets at equal specificity, which
is what "custom stylesheet" has always been documented to mean. Nothing could have relied on losing;
a user rule that today loses to a component sheet's ≤ 768 px rule wins after this, which is the
documented intent. `CustomCss` keeps its place (after everything) and its meaning. The wiki sentence
"When set, replaces the default stylesheet" (`Report-Configuration.md:110`) is false today (the base
sheet is always emitted) and is corrected in §8.

Rejected alternatives: raising the violet rules' specificity (fixes the built-in theme only, and every
user sheet stays broken); moving the theme-overridable component rules into the base sheet (more
churn, and the next component sheet reintroduces the defect). S3 is the structural fix: components,
then theme, then the popup theme (S5), then `CustomCss`.

### 3.4 S4: the violet constant (`Constants/Stylesheets.cs`)

Two things, in one constant. First the order: the four-toggle hover block (`:47-53`) moves above the
active block (`:34-41`), mirroring the base sheet, so an active filter toggle keeps its colour on
hover, and on a phone keeps it after the tap (the measured defect, §2.4). Second the rules the census
found missing on toolbar controls, every idle hover placed before any active rule it could shadow at
equal specificity, and every active rule after:

```css
/* idle hovers: before the active rules, which they must not shadow (same specificity, later wins) */
.export-btn:hover, .collapse-expand-all:hover, .percentile-btn:hover, .timeline-toggle:hover,
.details-radio-btn:hover, .scenario-diagram-controls-toggle:hover { background: #EDE9FE; border-color: #A78BFA; }
.diagram-toggle-btn:hover, .iflow-toggle-btn:hover { background: #EDE9FE; }
.dep-mode-toggle:hover, .cat-mode-toggle:hover { background: #EDE9FE; border-color: #A78BFA; }
.iflow-rel-summary-table tr:hover td { background: #EDE9FE; }
/* active states, after */
.percentile-btn.percentile-active { …as today…; border-color: #8B5CF6; }
.timeline-toggle-active { background: #8B5CF6; color: white; border-color: #8B5CF6; }
.timeline-toggle-active:hover { background: #7C3AED; }
.diagram-toggle-active:hover { background: #7C3AED; }
```

The tint is the one the constant already uses for the filter chips (`:47-51`); the active hover is
the one it uses for the flow toggle (`:68`). The tab and flow-toggle hovers set background only, as
the popup sheet does, so an active tab's violet border survives its hover. With S3 in place these are
what the prototype measured (§2.4, the "after" column; the C# prototype's constant is in
`prototype.diff`). The four states that stay blue (the scenario "#" link and copy-name button hover,
the parameterized-test table row hover and active) are not toolbar controls; Q3 recommends adding
them in the same pass and says what they are if declined.

### 3.5 Cascade and layout notes, so the executing session does not rediscover them

- Two blocks with the same selector and specificity: the later wins. Every rule this plan moves is
  moved for that reason; check order before specificity.
- Inside one sheet the same rule applies: an idle `:hover` rule (0,2,0) written after an active
  `.x.x-active` rule (0,2,0) shadows it while hovered. The violet constant had that defect for its
  four filter toggles, and S4 puts every idle hover first. On a touch screen a tapped control stays
  hovered, so this defect is on screen after every tap, not only under a mouse.
- The popup sheet's `:hover` rules are 0,2,0. A theme that wants an active hover must state it; a
  plain active rule never covers hover.
- Media blocks with disjoint ranges cannot combine; a `min-width` lower bound on the new block is
  what keeps them disjoint, and it is written `768.02px` because a viewport can be 768.8 px wide
  (§2.12). The 2026-08-31 mock lost a resize to an appended rule beating a media reset on source
  order (memory, "composed-CSS cascade traps").
- A `flex-shrink: 0` flex item is never squeezed, so `overflow-wrap` on its content does nothing
  until something bounds its width (S1b's cap).
- A flex item with a definite `width` (the search input's `width: 100%`) is already bounded by it; a
  `min-width: 0` on it changes nothing (§2.2).
- `content-visibility: auto` on `.feature` and `.scenario` clips everything that overflows them, and
  the clipped part never reaches the page's scroll width. A width fix inside a scenario has to be
  measured against the scenario's box, never only against the page (§2.9, §4).
- Buttons do not inherit the body font. A font stress on `body` alone leaves every label at the
  user-agent font (§2.7).
- `report-init-script.js:25` decides `isMobile` once, and at ≤ 768 px hides every scenario toolbar
  behind a "Diagram Settings" button (`:38-54`). A sweep that resizes without reloading tests the CSS
  but not the page a phone user gets, and one that does not open the toolbar measures nothing of it;
  the guard reloads and opens (§4). The no-reload sweeps were run too and are clean (§2.11).
- Playwright's Chromium hides scrollbars by launch argument; a desktop browser does not. Injected
  scrollbar CSS does not undo it (§2.11).
- The E2E default fixture (`ReportTestHelper.GenerateReport`, `:148`) has three dependencies, no CI
  metadata, no flow tracking and no component diagram. Its header row is 160 px roomier than a CI
  run's at every width (§2.2), a CI run with a short branch is 273 px roomier than one with a long
  branch (§2.6), and its toolbar and top bar carry fewer controls than a default run report's (§2.9,
  §2.10), so a sweep on it would pass over the worst bands. The guard uses its own fixture (§4).

### 3.6 What a reader sees change, for the changelog

1. Export button labels no longer wrap inside their buttons; the cluster wraps as a row instead.
2. From 769 to 1160 px the filtering box sits on its own row under the summary and CI boxes. At
   1161 px and above the header row is what it was; at 768 px and below the column layout is what
   it was. A void beside the summary is the cost, until 12.1 recomposes the band (Q1).
3. In the row layout the CI box is at most 20 em wide and wraps a long branch or repository name;
   the label column stays whole.
4. The scenario toolbar is styled in every report, flow tracking or not, with 16 px of side padding
   (§3.2).
5. A scenario toolbar that does not fit its line wraps onto the next, its trailing controls at the
   left, instead of wrapping its labels inside their buttons and, on a toolbar with many controls,
   cutting the last ones off out of sight (between 780 and about 1300 px on the published reports).
6. The top bar's buttons wrap as whole buttons; with the default component diagram their labels
   wrapped inside them between 500 and 580 px.
7. A custom stylesheet is applied after the built-in sheets, so its rules for the toolbar, the Details
   radio, the diagram tabs, the flow toggle and the related-tests list take effect; on the default
   violet `Specifications.html` those controls now paint violet, an active filter toggle keeps its
   colour on hover (and, on a phone, after the tap), and every toolbar hover is violet (§2.4). The
   order change alone moves no byte in a report without a custom stylesheet; the rules in items 1
   to 6 change every report's `<style>` block.
8. `InternalFlowPopupCustomStyleSheet` is applied, after the specifications theme and before
   `CustomCss`, when internal-flow tracking is on (S5). It had never been applied.

### 3.7 S5: the popup custom stylesheet (`ReportGenerator.cs:422`, `:427`)

`ReportConfigurationOptions.InternalFlowPopupCustomStyleSheet` (`:181`, "Custom CSS stylesheet for
internal flow popup windows"; wiki `Report-Configuration.md:154` and `Internal-Flow-Tracking.md:165`
promise "overriding or extending the default popup styles") has no consumer: `grep` over `src` finds
the declaration only, and `../Kronikol4J/docs/REMAINING_PARITY.md:1575` recorded the same as the
reason the port left it out. The two report call sites compose it into the `stylesheet` argument
they already pass, so that after S3 it lands after the theme and before `CustomCss`:

```csharp
/// The user stylesheets a report carries after its built-in sheets, in cascade order: the given
/// theme (the specifications sheet, or none), then InternalFlowPopupCustomStyleSheet when
/// internal-flow tracking is on. Null when there are none, so the report keeps its bytes.
internal static string? UserStylesheets(string? theme, ReportConfigurationOptions options)
{
    var popup = options.InternalFlowTracking ? options.InternalFlowPopupCustomStyleSheet : null;
    return popup is null ? theme : theme is null ? popup : theme + "\n" + popup;
}
// :422  GenerateHtmlReport(…, UserStylesheets(options.HtmlSpecificationsCustomStyleSheet, options), …)
// :427  GenerateHtmlReport(…, UserStylesheets(null, options), …)
```

Measured on the C# prototype (§2.14). The merge path (`MergeableReportRenderer.cs:65`) passes nothing,
as it passes nothing for the theme: the merge tool has no options object to read it from. Order among
the three user sheets: theme (general), popup sheet (specific), `CustomCss` (last, as documented).

Rejected alternative, the one the first pass wrote: a new optional `internalFlowPopupCustomStyleSheet`
parameter on the public `GenerateHtmlReport`. Adding a parameter changes the signature of a public
static method, which breaks callers compiled against the old one (a missing-method error, not a
compile error) and invites the question whether a parameter is new surface; the call-site composition
has neither cost and the same effect. Q6 asks only whether it ships in R1.

The other options the same ledger line calls inert (`InternalFlowDisplay`, `InternalFlowTrigger`,
`InternalFlowContentStrategy`, `InternalFlowFragmentsFolderName`) are not stylesheet work;
`INTERNAL_FLOW_BLOB_PLAN.md` F7 has them.

---

## 4. The sweep as a permanent guard

`tests/Kronikol.Tests.EndToEnd/ViewportSweepTests.cs`, in the `PlaywrightCollections.Mobile`
collection, named in `ci.yml`'s "E2E (Toolbar & Reports)" filter beside `MobileResponsiveTests`
(`ci.yml:91-93`) **and** added to the Remainder group's `filter-exclude` list (`:122`), whose comment
says it is the union of every class a named group runs: without that it would run twice.

**Browser.** The class launches its own Chromium once (a class fixture) with
`IgnoreDefaultArgs = ["--hide-scrollbars"]`, so a tall page gets the 15 px classic scrollbar a
desktop Chrome draws on Windows and Linux, which moves the bands by up to 20 px (§2.11). The
collection's shared browser keeps hiding them for every other test. At phone widths the scrollbar
makes the layout 15 px narrower than a phone's overlay scrollbar would, a harsher case that the
prototype passes.

**Pages.** `ReportTestHelper.GenerateReportWithWideHeader(tempDir, outputDir, fileName, specifications:
bool, internalFlowTracking: bool)`, new: the §2.1 data with the **long CI strings of §2.6** (through
the `ciMetadata` argument, `CiMetadata(CiEnvironment.GitHubActions, buildNumber, branch, sha, url,
repository, attempt)` as `gen.cs` builds it), seventeen participants, tags, a `database` participant,
a diagram source that carries an `<<assertionNote>>` and a `<<stepDelimiter>>`, whole-test flow
segments with `WholeTestFlowVisualization.Both` when flow tracking is on (the Sequence, Activity and
Flame tabs), `ShowNoteFontControls` on (the widest toolbar a reader can be given), and on the run
report a `componentDiagramPlantUml`, as every default run report with dependencies carries one; this
is `gen.cs -- full` with the long strings (`TestRunReport_full_longci.html`). `Stylesheets.VioletThemeStyleSheet`
on the specifications shape as `:2299` already does. Three facts: the run report with flow tracking,
the violet specifications with flow tracking, the run report without flow tracking (the F5 path,
where the toolbar is a block at HEAD). Long strings and every control, because each is the measured
worst case of its kind.

**Widths.** 320 to 1400 in **20 px** steps (55 widths; the narrowest band the harness found is
770 to 780 wide, so 20 px cannot step over one) plus **769 and 1161**, the first widths of the band
and of the row layout, where the box is narrowest. `SetViewportSizeAsync(w, 900)` then `ReloadAsync()`
and a wait for `details.feature` (`WaitForFunctionAsync` with `PollingInterval = 200`, per
`CLAUDE.md`), then every `<details>` opened by script, `.filters` shown, and at 768 px and below the
first "Diagram Settings" button (`.scenario-diagram-controls-toggle`, `.First`) clicked so the
scenario toolbar is measured visible; then one `EvaluateAsync` returning the measurement. The harness
takes 6.4 to 8.5 s per page for 55 widths with a reload at each (Node, this machine), so three facts
should take well under a minute locally and a few on the runner.

**Assertions, collected per width and reported together** so a red run names every bad width once:

1. `document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1` (`clientWidth`,
   not `innerWidth`, so the classic scrollbar is not counted as overflow).
2. No button in `.filtering-box-export`, `.toolbar-row` or `.diagram-toggle` has a text range with
   more than one client rect (the line-box count; `PlaywrightTestBase.GetPaintedSvgLines` uses the
   same idea for SVG text). `.toolbar-row` includes `.toolbar-left`, which the component diagram
   fills (§2.10).
3. `.filtering-box-export`'s right edge is not past `.filtering-box`'s.
4. Every visible `button` and `select` in every visible `.diagram-toggle` ends at or before that
   toolbar's right edge (+1 px). Assertion 1 cannot see a clipped control (§2.9); this one can.
5. On the two run-report facts (the specifications shape has no summary table), the composition D5
   chose: from 769 to the breakpoint the box's top is below the summary table's bottom and its width
   is the header row's content width; above the breakpoint the box's top equals the summary's top;
   at 768 and below `flex-direction` is `column` (already
   `MobileResponsiveTests.Header_row_stacks_vertically_on_mobile`).
6. On the two run-report facts, from 769 px up, `.ci-metadata` is no wider than 20 em plus its
   padding, and its first-column cells have one client rect each.

Playwright rules from `CLAUDE.md` that apply: no `Force = true` (the "Diagram Settings" button is an
ordinary button); real generated pages, no mocks; the polling interval; `.First` where a selector
matches per-scenario controls. Nothing here needs SVG events or the search bar.

**What the guard does not do.** It does not wait for diagrams to render (§2.2 says why the SVG is not
a width source); it runs Chromium only, at device scale 1, on the Ubuntu runner's fonts (§2.7 and
§2.11 say why engines and fonts only move the bands, and Firefox and WebKit were measured clean); it
cannot reach a fractional width (§2.12: T1 pins the bound); it does not assert the layout script's
inline-versus-stacked choice, which `DiagramToggleLayoutTests` holds at 1400 and 520; and it does
not look inside parameterized tables, whose clipping is Q7's.

---

## 5. Tests, in the order they are written

TDD per `CLAUDE.md`: each test is red at HEAD for the reason given, then the slice makes it green.

| # | Test | Red because | Green by |
|---|---|---|---|
| T1 | unit, `Kronikol.Tests/Reports/StylesheetRulesTests.cs` (new): a small helper that returns the declaration text of a selector's block (inside a given media block when named), and assertions that `.export-btn` has `white-space: nowrap`, `.filtering-box-export`, `.filtering-box-header` and `.toolbar-left` have `flex-wrap: wrap`, a `@media (min-width: 768.02px) and (max-width: 1160px)` block holds `.header-row { flex-wrap: wrap }` and `.filtering-box { flex-basis: 100% }`, `.ci-metadata table` has `max-width: 100%`, `.ci-metadata td:first-child` has `white-space: nowrap`, `.ci-metadata td:last-child` has `overflow-wrap: anywhere`, a `@media (min-width: 768.02px)` block holds `.ci-metadata { max-width: 20em }`, and no `min-width: 769px` query exists in any sheet (the gap, §2.12) | none of them exist | S1, S1b |
| T2 | unit, same file: `Stylesheets.HtmlReportStyleSheet` holds `.diagram-toggle` with `display: flex` and `flex-wrap: wrap`, the five other toolbar rules, and `DiagramContextMenu.GetInternalFlowPopupStyles()` holds no `.diagram-toggle` rule at all (each rule lives in exactly one sheet) | the rules are in the popup sheet | S2 |
| T3 | unit, `ReportGeneratorStyleOrderTests.cs` (new), two facts. (a) `GenerateHtmlReport` with a marker theme (`/*THEME*/`), `BrowserJs`, `internalFlowTracking: true` and a `customCss` marker: the theme marker's index is greater than the index of `.iflow-toggle-active:hover` and of `.details-radio-btn.details-active` and less than the `customCss` marker's; with `stylesheet: null` the `<style>` block holds no marker and the base sheet is followed by exactly one newline before the next sheet (the byte-keeping line). (b) `CreateStandardReportsWithDiagrams` with `HtmlSpecificationsCustomStyleSheet = "/*THEME*/"`, `InternalFlowPopupCustomStyleSheet = "/*POPUP*/"`, `CustomCss = "/*CUSTOM*/"` and flow tracking on: in `Specifications.html` THEME < POPUP < CUSTOM, in `TestRunReport.html` POPUP < CUSTOM and no THEME; with flow tracking off no POPUP in either (the shape `s5.cs` measured) | the theme is first; the popup sheet is never emitted | S3, S5 |
| T4 | unit, `StylesheetRulesTests.cs`: in `Stylesheets.VioletThemeStyleSheet` the `.happy-path-toggle:hover` rule precedes `.happy-path-toggle.happy-path-active`; every S4 idle-hover selector is present and precedes every S4 active selector; `.percentile-btn.percentile-active` has `border-color: #8B5CF6`; `.timeline-toggle-active`, `.timeline-toggle-active:hover` and `.diagram-toggle-active:hover` exist (and Q3's four, if taken) | the hover block follows the active block; the rules are absent | S4 |
| T5 | E2E, `ViewportSweepTests` (§4), three facts | the §2.2, §2.6, §2.9 and §2.10 bands | S1 + S1b + S2 |
| T6 | E2E, `MobileResponsiveTests`-style fact in a new `ToolbarStylingTests.cs`: a report without flow tracking, first scenario's diagrams opened, `.diagram-toggle` computes `display: flex`, `flex-wrap: wrap` and 16 px side padding | `block`, `nowrap`, 0 | S2 |
| T7 | E2E, same class, the census as a test: the fixture is `ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport` (`:2170`, a sequence diagram plus whole-test activity and flame views, so the Sequence / Activity / Flame tabs exist) extended with an optional `stylesheet` argument, generated with the violet sheet; computed background and border of each real control in the §2.4 table that the page has, at rest and after `HoverAsync` (the filter toggle after a click, for the active-hover row): every "after" cell of §2.4. The same page without flow tracking for the Details radio and the tabs. And the control: the same fixture with `stylesheet: null` reads the blue values of §2.4 | §2.4 | S3 + S4 |
| T8 | E2E, strengthen `MobileResponsiveTests.Diagram_toggle_wraps_on_mobile` (`:199`) to open the "Diagram Settings" button first and assert `display: flex` beside `flex-wrap: wrap` | it reads `flex-wrap` off a hidden `block` box today and passes vacuously, the class stage 0 removed elsewhere | S2 |
| T9 | E2E, `DiagramToggleLayoutTests` unchanged, run and green after S2 | (regression net for the inline float with `flex-wrap` and 16 px of padding; green on the C# prototype, §2.14) | S2 |
| T10 | E2E, `ToolbarStylingTests`: the T7 fixture generated with `InternalFlowPopupCustomStyleSheet = ".iflow-toggle-active { background: rgb(1, 2, 3); }"` through `GenerateReports`-shaped options, the whole-test-flow block's active toggle paints `rgb(1, 2, 3)` (over the violet theme), and with the option unset it paints violet | the option is never emitted | S5 |
| T11 | E2E, `ToolbarStylingTests`: the violet specifications in a context with `IsMobile = true`, `HasTouch = true`, 390 × 844; `TapAsync` the mobile filter toggle, then "Happy Paths Only"; while the button still matches `:hover`, its background is `rgb(139, 92, 246)` | it is `rgb(237, 233, 254)` under white text (§2.4) | S4 |

Then refactor: the helper in T1 replaces the bare `Assert.Contains` on CSS text in
`ParameterRenderingReportTests.cs:1152-1153` and `HeadersDetailsInterferenceReportTests.cs:164-175`
if it fits them cleanly, and not otherwise.

Full suite before the release: `dotnet test` on `Kronikol.Tests` and `Kronikol.Tests.EndToEnd`
(Playwright browsers installed), and the CI run of the pushed sha read before the next release starts.
The one-off byte diff of S3 and S5 on a null-sheet report (§3.3) is run before the suite and its
result written into §9. The prototype's figures for the existing suites (§2.14) are the expectation:
the whole of both passed on it unchanged.

---

## 6. Kronikol4J

`stylesheets.css` is "byte-shared" by construction (`Stylesheets.cs:21-22`, `JAVA_PORT_PLAN.md`
§4.2), and today three of the five copies already differ from .NET's (§1). D11 (roadmap §3)
recommends freezing the rendering half of the ledger with one entry that says so. This plan therefore
writes **one entry** under `../Kronikol4J/docs/REMAINING_PARITY.md` "`.NET`-side features shipped
after this audit (divergence ledger)" (`:1877`), in the form the 3.22.3 entry uses: the release
number, the date, what changed (the S1 and S1b rules and media blocks in `stylesheets.css`, with the
`768.02px` bound and the `.toolbar-left` wrap; the six rules leaving `internal-flow-popup-styles.css`
for it, with `flex-wrap`; the reordered and extended violet constant, which the port does not carry;
the custom sheet emitted after the component sheets, where `DotNetHtmlReportRenderer.java:320-330`
places it where .NET placed it until now, with the base sheet's trailing newline kept so run reports
without a sheet gain no byte from the move; and `InternalFlowPopupCustomStyleSheet` now applied, so
the ledger's `:1575` reason for not porting it no longer holds), and the sentence that the copies were
already two, one and one hunks behind before this release.

Mirroring (three CSS files copied and the one-line reorder in the Java renderer) is done in the same
session only if D11 is answered "mirror" (Q4); the entry says which it was.

---

## 7. Releases and bumps

`CLAUDE.md`: a fix is a patch, anything new for a consumer to call is a minor, and a fix that changes
observable behaviour is still a patch with the change called out. Nothing here is new to call: no
option, no type, no public member, no parameter (S5 composes an existing option at the call sites,
§3.7), no report control. The header band, the CI cap, the toolbar styling and wrapping, the top-bar
wrap, the sheet order and the applied option are observable changes and are listed in the changelog
(§3.6).

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| **R1** | S1 to S5, T1 to T11, the wiki and plan corrections, the CI filter lines, the ledger entry | **patch**, the next after whatever has shipped by then (3.29.2 if nothing else lands first; 3.29.1 shipped on 2026-09-24) | Fixes only; the behaviour that changes is the behaviour that was broken |

**In this order**: the full suite green; `Directory.Build.props:4`, `.claude-plugin/plugin.json:5`
and `.claude-plugin/marketplace.json:14` to the same number (`PluginManifestTests` holds the first
two equal); the changelog entry stating which part moved and why; the wiki; commit; tag `v3.x.y`;
push both; read the CI run of the pushed sha.

Track B rule from the roadmap: P3, P4 and P5 edit report output too, so only one of them re-pins
goldens at a time. P2 re-pins none: no stored golden HTML covers the stylesheet, the toggle-defaults
byte-identity test compares outputs of one build with each other, and the unit suite passed on the
C# prototype unchanged (§2.14).

---

## 8. Documentation

| Where | Change | Release |
|---|---|---|
| `CHANGELOG.md` | R1: `Fixed` entries with the measured figures (labels wrapping inside the export buttons at 380 px and below and from 770 to 1260 px, the 770 to 1060 px scroll and its 295 px peak, the 32 px box, the 780 to 1320 px scroll on a long branch, the scenario-toolbar controls clipped out of sight from 780 to about 1300 px on the published reports, the top-bar labels wrapping at 500 to 580 px, the blue Details radio and the pale active toggle on the default violet report, the unstyled toolbar without flow tracking, the never-applied popup stylesheet); the eight behaviour changes of §3.6 stated as changes | R1 |
| `../Kronikol.wiki/Report-Configuration.md:110` | `HtmlSpecificationsCustomStyleSheet` row: "When set, replaces the default stylesheet" is false; it reads "appended after the built-in stylesheets (base, context menu, inline SVG, notes, internal flow) and before `InternalFlowPopupCustomStyleSheet` and `CustomCss`; the default is a violet overlay on the base sheet, not a replacement for it" | R1 |
| `../Kronikol.wiki/Report-Configuration.md:154`, `Internal-Flow-Tracking.md:165` | `InternalFlowPopupCustomStyleSheet` rows: "applied after the built-in sheets and the specifications theme, before `CustomCss`, when internal-flow tracking is on (from R1; earlier releases did not apply it)" | R1 |
| `../Kronikol.wiki/API-Reference.md:68` | `Stylesheets` row: `VioletThemeStyleSheet` is "the violet overlay that is the default `HtmlSpecificationsCustomStyleSheet`", not "the default HTML report stylesheet"; `HtmlReportStyleSheet` is the base sheet | R1 |
| `../Kronikol.wiki/Generated-Reports.md:452` (Report Features, the **Custom branding** bullet) | one bullet after it on how the report lays out by window width: column at 768 px and below, the filtering box on its own row from 769 to 1160 px (per D5), the full header row above, the CI box capped at 20 em; the toolbars wrap their controls rather than their labels, in every report | R1 |
| `src/Kronikol/ReportConfigurationOptions.cs:52` | the doc comment gains "appended after the built-in stylesheets, before `InternalFlowPopupCustomStyleSheet` and `CustomCss`" (a doc comment ships: patch) | R1 |
| `src/Kronikol/ReportConfigurationOptions.cs:180` | the doc comment gains "applied after the specifications theme, before `CustomCss`, when `InternalFlowTracking` is on" | R1 |
| `.github/workflows/ci.yml:93`, `:122` | `ViewportSweepTests` in the "E2E (Toolbar & Reports)" filter and in the Remainder's `filter-exclude` | R1 |
| `plans/TOOLBAR_REDESIGN_PLAN.md` | §2.1 marked shipped with the release number; §5 question 6 answered per D5; §1's F5/F6 row and the "Two shipped bugs" paragraph gain the §2.4 correction (the theme is emitted first; the Details radio is blue on the default report; the theme's own hover order); F11 gains §2.9 (the full toolbar is clipped, not only squeezed, by `content-visibility: auto`); §2.7's "F5 regression" and "§2.1 sweeps become permanent guards" point at `ViewportSweepTests`; F4 (colour drift) gains the four Q3 states by name if they were declined | R1 |
| `plans/ROADMAP.md` | 1.5 marked shipped with the number; D5 recorded as taken with the breakpoint chosen; the "How it looks" bar row's "two toolbar defects and the unstyled-toolbar defect" ticked; Q7's parameterized-table clipping added where the owner places it | R1 |
| `plans/INTERNAL_FLOW_BLOB_PLAN.md` F7 | a pointer that `InternalFlowPopupCustomStyleSheet` was wired by this plan, so F7's list of inert options is four | R1 |
| `plans/PLANS_STATUS.md`, `plans/STAGE_1_PLAN.md` | the rows and the P2 line updated | R1 |
| memory `toolbar-redesign-investigation.md` | F6's description corrected (done with this plan, 2026-09-22) | now |

---

## 9. Log

**Executed 2026-09-24 as 3.29.2**, on the owner's "implement the plan in full": the §10 recommendations
taken as the answers. D5 = yes at **1160 px**; Q2 yes; Q3 in part (item 1 below); Q4 ledger only (D11 is
unanswered); Q5 as recommended; Q6 wired as a fix. Q7 is not in this release: it is a sibling defect the
plan scoped out (§12), recorded as roadmap 1.10 with its placement left to the owner.

**What shipped.** S1, S1b, S2, S3, S4 and S5 as §3 gives them, in `stylesheets.css`,
`internal-flow-popup-styles.css`, `Constants/Stylesheets.cs` and `ReportGenerator.cs`; the doc comments
of `HtmlSpecificationsCustomStyleSheet`, `InternalFlowPopupCustomStyleSheet`, `CustomCss` and
`VioletThemeStyleSheet`; the four wiki pages of §8; the two `ci.yml` lines (with `ToolbarStylingTests`
beside `ViewportSweepTests` in the "Toolbar & Reports" group and the Remainder's exclusion); one
Kronikol4J ledger entry. Tests: `StylesheetRulesTests` (T1, T2, T4) over a small rule reader,
`CssRules`; `ReportGeneratorStyleOrderTests` (T3 a and b, and `UserStylesheets` by case);
`ViewportSweepTests` (T5); `ToolbarStylingTests` (T6, T7, T10, T11); the strengthened
`MobileResponsiveTests.Diagram_toggle_wraps_on_mobile` (T8); `DiagramToggleLayoutTests` (T9) unchanged
but for its polling interval.

**Measured at execution** (the release worktree, `afeafa5b` plus this change):

| Check | Result |
|---|---|
| New unit facts on 3.29.1 (with a stub for the helper that did not exist) | 19 of 26 red; the 7 green are the pins that hold on 3.29.1 too (the byte-keeping newline, the popup sheet's absence without flow tracking, the helper's pass-through cases) or that are trivially true until a `min-width` bound exists |
| New and changed E2E classes on 3.29.1 (`ViewportSweepTests`, `ToolbarStylingTests`, `MobileResponsiveTests`, `DiagramToggleLayoutTests`) | 23 of 75 red: all three sweep facts (280, 1,050 and 1,315 problems on the no-flow run report, the violet specifications and the flow run report: sideways scroll from 769 px, clipped toolbar controls from 769 to 1280 px, labels wrapped in every band of §2.2, the CI box 547 px), every violet census row but the dependency-chip hover (already violet), the blue report's tabs without flow tracking (unstyled, F5), T6, both T10 rows and the violet T11. The 52 green are the other blue-report controls, T8 and the untouched facts |
| The same on the release | 75 of 75 green |
| The guard, locally | 57 widths in 8.9 to 9.5 s a page; the classic scrollbar present (15 px) at every width |
| The guard on CI (`ubuntu-latest`, the release commit `10c12ec8`) | green; its three facts took 22, 16 and 12 s, and the whole "E2E (Toolbar & Reports)" group, 97 tests with `ToolbarStylingTests` and the guard, 1.1 min. CI, Release, CodeQL and CI Summary Preview green on the release commit |
| §3.3's byte check (`s3bytes.py`, 3.29.1 and the release built from LF checkouts of one repository) | the six shapes without a custom sheet: identical once the base and internal-flow sheets are swapped for the new ones. The three violet shapes: identical once the old and the new theme text are cut out of each, so the theme moved and nothing else did (`s3bytes-result.txt`) |
| `sweep2.js` on the release build's nine shapes, 20 px, with and without classic scrollbars | clean on every column (no scroll, no wrapped label, no escaped cluster, no clipped control, no clipped content) |
| Unit suite | 5,525 passed, 1 skipped (the 100 MB streaming test, skipped on 3.29.1 too), 0 failed |
| E2E suite without the three doc-asset generators | 818 passed, 0 failed, 7 min 45 s (778 before, plus the 40 new facts) |

**Departures from the plan, and why:**

1. **Q3 in part.** The scenario "#" link's and copy button's hover and the search-match marker
   (`tr.row-search-match`) are violet. The parameterized row's hover and `row-active` tints are not
   overridden: every emitted row carries a status class (all five `ExecutionResult` values map to one,
   `ReportGenerator.cs` at the two row emitters), whose rule of equal specificity follows the hover and
   active rules in the base sheet, so neither blue tint paints on a real row. The census read them on
   synthetic rows without a status class. A violet rule emitted after the base sheet (S3) would win over
   the status tint instead and paint lavender over a passed row's green on hover, which the blue report
   never does. Found on the way and not fixed: the base sheet's own row hover is therefore dead on every
   real row, so a clickable row gives no hover feedback. Which colour a hovered passed row should show is
   a design choice; it is recorded under roadmap 1.10 with Q7.
2. **T10 reads a flow toggle appended to the page**, not one in "the whole-test-flow block": that block
   has no emitter (`InternalFlowHtmlGenerator.GenerateWholeTestFlowHtml` has no caller), and the flow
   toggle exists only inside the popup, which a fixture cannot open without a rendered diagram and its
   arrow links. The appended element carries the real classes (the census's method) on a page whose
   `<style>` was composed by `ReportGenerator.UserStylesheets` from real options, which is what the two
   call sites do; T3 b pins the order through `CreateStandardReportsWithDiagrams`.
3. **T8 is green on 3.29.1 as well.** After the "Diagram Settings" tap the init script sets
   `display: flex` inline and the notes sheet's ≤ 768 block already wraps, so strengthening the fact
   removed its vacuity (it read a hidden toolbar) without making it red. It also asserts that no control
   ends past the toolbar's edge.
4. **T7 is seven theories and a phone-width fact** (the filter toggle's active hover, the header hovers,
   the active percentile and timeline buttons, the Details radio, the tabs with and without flow
   tracking, the scenario link and copy button, the popup controls and the search-match marker), each
   with the blue report as its control, and the "Diagram Settings" hover at 700 px. Colours are read
   with `ToHaveCSSAsync`, which retries, so no transition timing is involved.
5. **An empty string counts as no sheet** in `UserStylesheets` and in S3's `themeStyles`, so
   `HtmlSpecificationsCustomStyleSheet = ""` (one way to switch the violet theme off) keeps its bytes as
   well; pinned in T3.
6. **Fixed in passing:** `DiagramToggleLayoutTests` waited without `PollingInterval = 200` (four calls),
   which `CLAUDE.md` requires; §5's refactor step fitted three facts in `ParameterRenderingReportTests`
   and `HeadersDetailsInterferenceReportTests`, which now read the rule instead of a substring.
7. **The shipped CSS comments cite no plan**: the stylesheet ships inside every report and is
   byte-shared with the port, so they say why without a file reference.
8. **A trap for whoever repeats this:** a fresh `git worktree add` on this machine (`core.autocrlf=true`)
   checks the sources out with CRLF where the main checkout has LF, and C# raw strings take their line
   breaks from the source file, so a build there emits different bytes. The release worktree was checked
   out again with `-c core.autocrlf=false -c core.eol=lf`; `git apply` and `git checkout --` without those
   flags write CRLF again.
9. **No pointer in `INTERNAL_FLOW_BLOB_PLAN.md` F7.** §8 asked for one on the premise that F7 listed
   `InternalFlowPopupCustomStyleSheet` among its inert options. It does not: F7 names three
   (`InternalFlowContentStrategy.SeparateFragments`, `InternalFlowDisplay.Inline`,
   `InternalFlowTrigger.Hover`); the five-option list is the Kronikol4J ledger's, which the ledger entry
   now answers for this one.

The CLI paths: `kronikol merge` renders with `stylesheet: null` (§1), so a merged report gets the new
rules and no theme, as before; `kronikol ingest` writes `Specifications.html` through
`CreateStandardReportsWithDiagrams`, so its violet theme now follows the component sheets too, and it
turns internal-flow tracking off, so S5 never applies there.

### Q7 and the parameterized row's states, executed 2026-09-24 as 3.29.3

On the owner's "fix that" for the dead row hover (item 1 above) and for Q7. It shipped as a patch: CSS, plus
the wrapper the report now puts around every parameter table.

**What shipped.**

- **Wide content.** Above the phone layout a grouped table without a flat view had no scrolling wrapper.
  Now every parameter table sits in `.param-table-wrapper`. Seven more boxes scroll sideways:
  - `.step-param-table` and `.step-param-combined-table`;
  - `.step-docstring` (its rule restored, below);
  - `.example-image` at every width, not only at 768 px and below;
  - `.raw-plantuml pre`;
  - `.features-summary-table-wrapper`;
  - `.test-execution-summary`.
- **Long tokens.** `.feature { overflow-wrap: anywhere }` breaks a long token in any text a feature
  holds. `.feature table, .error-diff { overflow-wrap: normal }` keeps table columns from splitting words
  that fit.
- **Attachment image.** The 320 px cap moved onto the link, which also stays within its step.
- **Labels.** `span.label` lost `white-space: nowrap`.
- **The stray text.** `rgb(100, 100, 100)` after the lightbox rule is gone. It made every browser drop
  the `.step-docstring` rule.
- **Row states.** Each status gets a hover tint and a selected tint.
- **Tests.**
  - `ViewportSweepTests`: every `details` element opened; a check that nothing runs past the feature or
    scenario holding it; a page of every content kind; that page swept again under text spacing.
  - `ScenarioContentWidthTests`.
  - The row states in `ParameterizedGroupTests`.
  - `StylesheetRulesTests`: the new rules, and that every selector is well formed.
  - `ParameterizedGroupRenderTests`: the wrapper.

**Measured.** The census is `census.js` in the harness (§U), with every `details` element open and
every scenario laid out:

| Check | Result |
|---|---|
| Census of the three published reports (3.29.0, the toolbar left out: 3.29.2 fixed it) | Three culprits. A step in a LightBDD parameterized row's detail panel: +493 px, 320 to 800 px. A nested sub-step on all three reports: up to +270 px, 320 to 560 px. The grouped table on all three: +143 px, 780 to 920 px. Measured without a classic scrollbar; with one each band reaches 20 px further (`census-published-head-scrollbar.txt`). |
| Census of the new test page on 3.29.2 | 20 culprits, with elements and text runs both measured (the full list is in the harness). |
| Census on the release, classic scrollbar, 20 px steps | Clean: the three published reports (new sheet injected, grouped tables wrapped as the emitter now wraps them), with and without text spacing; the test page in Chromium, in Firefox, in WebKit, and under text spacing |
| The new E2E facts on 3.29.2 (in the release worktree before the fix) | The sweep of the new page: 195 problems (225 under text spacing). The two run-report pages scroll sideways from 769 to 900 px once their features summary is open. Every `ScenarioContentWidthTests` fact red but the 1400 px image case, which holds on 3.29.2 and pins that the desktop image and its link are unchanged. The four row-state facts red. |
| Unit suite | 5,542 passed, 1 skipped (the 100 MB streaming test, skipped on 3.29.2 too), 0 failed |
| E2E suite without the three doc-asset generators | 838 passed, 0 failed, 7 min 48 s (818 before, plus the 20 new facts) |
| CI on the release commit `b74c8ddc` | CI (28 of 28 jobs), Release, CodeQL and CI Summary Preview green. The "E2E (Toolbar & Reports)" group took 3.2 min (1.1 on 3.29.2), with `ScenarioContentWidthTests` and the two new sweep facts. The groups run side by side and E2E (Remainder), at 6.5 min, is still the slowest, so CI as a whole takes no longer |

**Departures from §10 Q7, and findings.**

1. **The detail-panel defect is step text, not a sub-table.**
   - What was clipped in LightBDD's detail panels was `span.step-text` holding a parameter's `ToString()`
     (``System.Collections.Generic.List`1[BreakfastProvider...]``), 740 px wide at 400 px.
   - Ordinary scenarios lose nested sub-step text the same way: a lambda, a telemetry identifier.
   - So Q7's `.param-detail-panels { overflow-x: auto }` would have fixed one place and left the rest. A
     sideways scroll is also a poor way to read a sentence. Text breaks instead.
2. **The harness saw only part of it**, three ways:
   - Its clipped-content measure (`scrollWidth` on `.feature` and `.scenario`) read nothing from an
     off-screen scenario, which `content-visibility` does not lay out. Which scenarios it measured
     depended on the scroll position.
   - It measured elements only. A long token directly inside a block (a scenario name in its summary, a
     comment, a tree value, the feature's floated endpoint) overflows its line without widening any
     element.
   - It opened only features, scenarios and diagram blocks. With every `details` open, the features
     summary scrolls the page from 769 to 900 px on the sweep's run reports.
   The guard lays every holder out, measures text runs and opens everything.
3. **Every kind of content was affected, not only Q7's two.** The test page found:
   - step and combined tables;
   - server-rendered diagrams and their source;
   - attachment images and file names;
   - labels;
   - names, descriptions, rule names and the endpoint;
   - the doc string, whose rule the stray text had dropped since before the stylesheet moved into its
     own file (June).
   `Every_selector_in_the_built_in_sheets_is_well_formed` catches that kind of stray text now.
4. **The table is wrapped, not turned into a block.** Q7 offered both. `display: block` above 768 px would
   shrink every narrow table to its content (the anonymous table inside a block is sized to fit), a
   visible change to every report. The wrapper costs one element. The flat view's wrapper already
   showed the one layout change: the gap under a grouped table is now the flat view's, 15.2 px instead
   of 8.0, because a scroll container keeps its child's margin.
5. **The hover colours**, the design choice item 1 above left open.
   - Each status darkens its own tint. The steps in CIE L* are 2.3 to 3.1 for hover and 4.7 to 6.1 for
     selected.
   - Passed and failed keep their selected tints.
   - The selected skipped tint was paler than the resting one (L* 97.9 against 97.6), so a selected
     skipped row looked unselected. It is now amber, `#fce9b8`; `#f4e9be`, derived by formula, rendered
     khaki.
   - Bypassed had no selected tint and has `#dfe0fb`.
   - The selected rules follow the hovers, so a hovered selected row keeps its selected tint.
   - Swatches: harness `swatch.png`.
6. **The image cap moved to the link.** `max-width: min(320px, 100%)` on the image fitted a phone. But
   a percentage in `max-width` counts as none when the link sizes itself, so at desktop width the link
   grew to the image's natural 642 px beside a 322 px image, and the empty space still opened the
   lightbox (`probe-img2.js`). On the link, `max-width: min(322px, 100%)` sizes it against its step.
   The image, `border-box` with `max-width: 100%` and `max-height: 242px`, keeps its 320 by 240 px
   content exactly.
7. **The 1,160 px breakpoint has an edge §10 Q1 did not measure.**
   - Q1 calls 1,160 px clean under text spacing with a classic scrollbar "bar one width". Its runs
     stepped 10 px from 1,080 px, so 1,161 to 1,169 px were never measured.
   - There, with both conditions, the in-row filtering box is 189 to 198 px wide, too narrow for a
     letter-spaced "Export Filtered HTML" (185 px) and its padding. The cluster runs up to 11 px past
     the box through 1,172 px, and at 1,161 px the page scrolls sideways by 3 px (`probe-1161.js`). It is
     clean from 1,175 px.
   - Not changed: moving the breakpoint is D5's to decide. The text-spacing sweep therefore runs in the
     shared browser without the classic scrollbar, which is how D5 was measured, and says why.
8. **A test that could not fail, caught before shipping.** The server-diagram fact first compared
   `scrollWidth` with `clientWidth`. That holds just as well for content running out of a box that does
   not scroll, so it passed on 3.29.2. It now also requires `overflow-x` to be `auto` or `scroll`.

---

## 10. Open questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 = **D5** | The band wrap between 769 px and a breakpoint: yes or no, and the number | **Yes, at 1160 px** (the first pass said 1100). Without the band the CI-run report scrolls sideways from 770 to 920 px whatever else is fixed (§2.5). On the number: 1160 is the smallest breakpoint measured clean under every condition of §2.11, including the WCAG 1.4.12 text-spacing override with a classic scrollbar, bar one width where a long branch under both stresses edges the cluster past its box without scrolling the page. At 1100 every condition is clean except the text-spacing override, under which the in-row box at 1101 to 1160 px (163 to 183 px) is narrower than one letter-spaced export button and the page scrolls sideways by up to 29 px (54 with a scrollbar on a long branch). The cost of 1160 over 1100 is the void beside the summary and CI boxes on windows of 1101 to 1160 px (about 390 px of empty band at 1150, screenshot); the cost of 1100 is that text-spacing band, which leaves content reachable by scrolling and so does not fail 1.4.12, but is the kind of sideways scroll this plan exists to remove. At 1000 px a CI-run report's box is 225 px wide in the row, one chip per line. If 1100 or 1000 is chosen, one number changes in S1, T1, T5 and the wiki bullet |
| Q2 | Is F11's `flex-wrap: wrap` on `.diagram-toggle` in scope, given 1.5 does not name it | **Yes.** It is a precondition of F5 (§2.3), and on its own it fixes the worse half of F11: a toolbar with many controls clips the last ones out of sight from 780 to about 1300 px on the published reports (§2.9). One declaration, measured both ways, and 6.2 wants it anyway |
| Q3 | The four blue states on a violet page that are not toolbar controls: `.scenario-link:hover` and `.copy-scenario-name:hover` (`stylesheets.css:547`, `:566`, the tint `#EDE9FE`), `.param-test-table tbody tr:hover` (`:1533`, `#EDE9FE`), `tr.row-active` (`:1536`, `#DDD6FE`) and `tr.row-search-match` (`:1546`, a `#8B5CF6` inset shadow) | **Include in S4**, pinned by T4 and T7: the same class of defect, in the same constant, found by the same census, and `CLAUDE.md` says fix what is found on the way. Five lines. If declined they go to 6.2's F4 (colour drift) by name, with the census table as its evidence. The failure-cluster link colour is not proposed: run reports never carry the theme |
| Q4 | Kronikol4J: ledger entry only, or also mirror the three CSS files and the renderer's order | **Ledger only**, per D11's recommendation to freeze the rendering half; the entry records that the copies were already behind. If D11 is answered "mirror", the mirror is four file edits in the same session |
| Q5 | The guard's step, browser and CI group | 20 px plus the two band edges, three facts, its own Chromium with classic scrollbars, named in the "Toolbar & Reports" filter and excluded from the Remainder. If a lane's time matters, 40 px still brackets the measured bands (every band starts at 770 or 780, which a 40 px step from 320 lands on at 760 and 800; 20 is the safe choice). If a second browser launch is unwelcome in the Mobile collection, the shared one works with the bands 15 px narrower than a desktop's; say which in §9 |
| Q6 | `InternalFlowPopupCustomStyleSheet` (S5): wire it in R1 | **Wire it in R1 as a fix, patch.** The option is the public surface and has existed since internal-flow tracking; the call sites compose it into the argument they already pass (§3.7), so no signature changes and nothing new is callable. If declined, the option is marked `[Obsolete]` with the reason and the two wiki rows say it is not applied, which is the smaller honest fix |
| Q7 | The parameterized-test content the published reports clip (§2.13): the grouped table past its scenario at 780 to 840 px, the LightBDD row detail panels at 320 to 760 px | **Its own patch, after R1, not in it** (the sibling a plan records, per the roadmap's rule that a recommendation is not a green light). Same mechanism as §2.9, different elements and different tests: `.param-test-table` a scroll container at every width, as it already is at ≤ 768 px (`stylesheets.css:1807`), or wrapped the way the flat view is (`.param-table-wrapper`, `:1578`); `.param-detail-panels` (`:1611`) `overflow-x: auto`; a sweep assertion that no `.scenario` or `.feature` holds content wider than itself, which would have caught §2.9 too. The same audit covers the run summary table's 19 px under text spacing at 320 px (§2.11). If the owner prefers, it joins 6.2. **Executed as 3.29.3** (§9, second part): the table wrapped, the detail panels' defect fixed as the step text it was, and every other kind of content the sweep found |

---

## 11. What is not known

- Safari on macOS and iOS, and any phone's own browser, were not measured; WebKit 26.4 on Windows
  (the Playwright build) is the proxy, and it measured clean on the prototype. The bands on a real
  device will differ by tens of pixels.
- Text-only zoom (Firefox's "Zoom text only") was not measured. The text-spacing override and the
  Verdana stress are the nearest measured stresses.
- Fractional widths were driven in Firefox only; the gap was measured there and closed by the bound.
  Chromium's fractional widths were seen but not driven into the gap (§2.12).
- The Server, Local and InlineSvg renderings were not swept; their toolbars carry only the whole-test
  flow toggles, and the Details radio and filter toggles are `BrowserJs`-only.
- The guard itself was not written; its run time is estimated from the harness's (§4). Its first CI
  run is the measurement, recorded in §9.
- The void beside the summary at 769 to 1160 px was eyeballed at 770, 900, 1050 and 1150 px (§2.8)
  and judged acceptable for a patch; it was not shown to anyone else.
- A CI box with a value longer than 20 em and no hyphen or slash was not screenshotted;
  `overflow-wrap: anywhere` breaks it, and the sweep would report any overflow.
- Whether any consumer sets `InternalFlowPopupCustomStyleSheet` today is unknown; if one does, S5
  changes that consumer's popup on upgrade, which is why the changelog names it.
- Whether anything besides the parameterized tables overflows a scenario on reports other than the
  three published ones is unknown; Q7's sweep assertion would find it.

---

## 12. What this plan does not do

Option B's segmented toolbar and everything in `TOOLBAR_REDESIGN_PLAN.md` §2.2 to §2.6 (6.2, which
also owns `aria-pressed`); findings F1 to F4 and F7 to F10 (F4's four non-toolbar states are Q3's); the
≥ 1351 px band grid, the container queries, the phone export-cluster grid and the `--kron-*` token
layer (Option C, 11.2 and 12.1); the parked page-shift-on-toggle bug; the Kronikol4J port work itself;
the four other inert internal-flow options (`INTERNAL_FLOW_BLOB_PLAN.md` F7); the dead
`.collapse-all-notes-btn` and `.toggle-headers-btn` rules in the popup sheet; the clipped
parameterized tables and detail panels and the run summary table under text spacing (Q7); the
once-at-load phone decision in `report-init-script.js` and the "Diagram Settings" buttons it leaves
behind when a phone is rotated to a wide window (§2.11); `content-visibility: auto` itself, which
stays for the render performance it was added for; any change to markup, to the URL hash, to the
filter JavaScript or to the selector names the §2.0 compatibility contract freezes.
