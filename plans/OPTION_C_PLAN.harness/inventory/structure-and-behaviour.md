# Option C preview: inventory of page structure and behaviour (for OPTION_C_PLAN.md section 3.2)

Written 2026-10-18 from the round 17 sources, read in full: `cmock/build.js`, `build-r9.sh`, `compose.py`,
`c-dom.r5.js`, `c-layout.js`, `c-round6.js`, `c-round8.js`, `c-round9.js`, `c-round9-tips.js`, `c-round13.js`,
`c-round16.js`, `c-round17.js`, and the three files build.js splices into the report's own scripts
(`c-round16-notes.js`, `c-round16-strips.js`, `c-round17-buttons.js`). `c-diagram-look.js` is out of scope (the
diagram-pass inventory covers it); its block headers were checked and every block in it is diagram
post-processing, so nothing in it belongs here. Product mapped against the `v5` worktree
(`C:\Code\Kronikol-v5`, HEAD `9452ddb5`, 4.26.9 plus the OR default).

Test counts are fixed-string greps over `*.cs`, excluding `bin/` and `obj/`, written as "lines/files".
"E2E" is `tests/Kronikol.Tests.EndToEnd` (131 files); "core" is `tests/Kronikol.Tests`.

---

## 0. How the mock is assembled, and what that means for the port

### 0.1 Files and load order

`build.js` turns a 4.26.4 report (`bp2-TestRunReport.html`, BreakfastProvider, 214 scenarios) into the preview:

1. strips the report's `<!doctype>/<html>/<head>/<body>` tags (the Artifact publisher adds its own skeleton);
2. patches the report's own scripts as text (section 2.7 below);
3. writes, in order: `<title>`, the theme boot script, the engine swap, the report's head content, `<style id="kronikol-c">` (c-skin.css), the preview banner, the report's body, then
   `<script id="kronikol-c-dom">` (c-dom.js) and `<script id="kronikol-c-layout">` (c-layout.js) at the end of the body.

`c-dom.js` is exactly the concatenation below (verified with `cmp`). To turn a part's line number into a c-dom.js line,
add the offset.

| Part (in order) | Lines | Offset into c-dom.js | Round |
|---|---|---|---|
| c-dom.r5.js | 675 | +0 | base transform, rounds 1 to 5; round 10/11/16 label |
| c-round6.js | 73 | +675 | 6 |
| c-diagram-look.js | 445 | +748 | (out of scope) |
| c-round8.js | 253 | +1193 | 8 (and round 17 item 11) |
| c-round9.js | 151 | +1446 | 9 |
| c-round9-tips.js | 162 | +1597 | 9 (tooltips), 16 |
| c-round13.js | 145 | +1759 | 13 (and round 17 item 20) |
| c-round16.js | 262 | +1904 | 16 |
| c-round17.js | 123 | +2166 | 17 (items 2, 21, 23) |

### 0.2 c-layout.js: included, superseded, or neither?

Neither. `c-dom.r5.js` does not contain or replace `c-layout.js`; build.js loads it as its own script after c-dom.js
(build.js L13, L274), and its header says it "Runs after c-dom.js". It is byte-identical to `c-layout.r4.js`, so it has
not changed since round 4 (its sections 4 to 6, the Timeline and Components controls, were spliced in from
`c-layout-tabs.js.part` by `splice-tabs.py` on 2026-10-17 before round 4; that part file is not loaded). The two files
talk through the DOM and one event: c-dom.r5.js dispatches `kron-filter-change`, which c-layout.js consumes, and
c-dom.r5.js reads `.report-nav`, which c-layout.js creates. Rounds 6, 8, 9, 13 and 17 build on c-layout's DOM
(`.features-tools-line`, `#features-sort`, `.report-nav`, `.report-tabs`, `.view-switch`, `.component-header`). It is
therefore part of the inventory (D3, D4, D20, D21, D27, D28).

### 0.3 Timing

- `c-dom.r5.js` and `c-layout.js` run synchronously at the end of `<body>`, before `DOMContentLoaded`.
- Rounds 6, 8, 9, 9-tips, 13 (hatch, chip), 16 and 17 run at `DOMContentLoaded` plus `setTimeout(0)`, that is AFTER
  the product's own `DOMContentLoaded` handler (`report-init-script.js`: mobile folds, then `parse_url_hash`). Some
  mock code exists only to undo or redo what that handler did first (D16 re-runs a `q=` search; D34 removes the
  mobile gear the product inserted). A port that emits the final markup drops all of that.
- The product's scripts are global function declarations, so the mock overrides them by assigning `window.X`
  (`filter_dependencies`, `filter_happy_paths`) or wraps them (`clear_all_filters`, `applyVisibility`,
  `run_search_scenarios`, `_preProcessSource`). Every such override is a place where the port edits the product
  script itself.

### 0.4 Harness gap (fix before porting)

`C:\Code\Kronikol-v5\plans\OPTION_C_PLAN.harness\mock\` has no `c-layout.js`, `c-round16-notes.js` or
`c-round16-strips.js`. build.js reads all three (L13, L117, L118), so the preview cannot be rebuilt from the plan's
harness, and the largest piece of page structure (c-layout.js) is missing from the record. Every other mock file
compared here is byte-identical to the scratchpad copy.

---

## 1. The page the generator should emit (desktop, after every round)

D-numbers say which item puts each node there. `[hidden]` nodes are legacy elements the mock keeps only so the
report's scripts still find them; the port does not emit them.

```
html[data-theme]                                   D1 (the .kron-c class, D2, is preview-only)
  head: theme boot script before the stylesheet    D1
  body
    div.custom-logo                                (unchanged)
    div.title-line > h1 + button.theme-toggle      D1
    section.run-band[aria-label="Test execution summary"]           D3, D4
      div.run-ring > svg (the pie)
      div.run-zone.run-summary > div.run-head(h2.run-title, span.run-status, span.run-when) + dl.run-stats
      div.run-zone.run-ci > div.run-head(h2.run-title "CI", span.run-when) + dl.run-stats
    div.filtering-box#filtering-box.rows-folded[.help-open]          D5, D15
      div.filtering-box-header > h2 + div.filtering-box-export
        span.filter-readout[role=status], Clear All, Export <span.wide-only>Filtered </span>HTML, ... CSV   D13, D14
      div.filter-search > input#searchbar, button.search-help-toggle, button.more-filters, (span.kron-deep-chip) D15-D17
      div.mobile-filter-line > div.mobile-filter-toggle                D14
      div.filters#filter-rows
        div.search-help-panel (+3 qualifier rows, +1 note)            D16
        div.filter-row[.pair-fits][.trio-fits]                        D7, D18
          div.status-filters > label + span.fltr-controls             D6
          div.duration-filters > label + span.fltr-controls           D6, D7, D19
          div.happy-filters > label + span.fltr-controls[role=group] > button.happy-seg x3   D8
        div.tag-filters > label + span.fltr-controls(.tag-mode-toggle, .tag-toggle..., Untagged, .group-clear)  D9, D11, D12
        div.dependency-filters > label + span.fltr-controls(.dep-mode-toggle, chips, No dependencies, .group-clear) D10-D12
        div.category-filters > label + span.fltr-controls             D6, D11
    div.nav-mark, div.nav-float-sentinel[aria-hidden]                  D24
    div.toolbar-row.report-nav[.on-features][.features-table-mode][.tools-open][.has-filters][.k-floating][.is-pinned]  D20
      (div.filtering-box while it rides in the bar)                    D24
      div.nav-row[.nav-compact][.nav-minimal]                          D24
        div.report-tabs[role=tablist] > button.report-tab[role=tab] x5 (Features n, Timeline, Components, History, Diagnostics n)  D20
        div.nav-filters[role=group] > span.nav-readout, button.nav-clear, button.nav-filters-toggle   D24
      div.features-tools                                               D21
        div.toolbar-left.features-tools-line
          div.view-switch[role=tablist] > button.view-btn[role=tab] "Full", "Summary"   D21
          span.k-fold-group x2 (features, scenarios)                   D22
          span.tools-spacer
          button.k-flag.k-scenario-settings-toggle                     D23
          label.sort-group > span.sort-label + select#features-sort     D21
          button.tools-gear (phone)                                    D35
        div.toolbar-right                                              D30-D32, D37, D38
          span.details-radio | span.diagram-format | span.diagram-flags[.zone-adjacent]
    div.report-panels                                                  D20
      div.report-panel#panel-features[role=tabpanel]
        div.filters-empty[hidden]                                      D24
        div.features-panel > div.features-table-view#features-table-view[hidden] (the summary table)  D21
                           + div#report-content (features, scenarios)
      div.report-panel#panel-timeline > div#scenario-timeline (header + controls, filtered note, rows)  D27
      div.report-panel#panel-components > div#component-diagram (header + info + controls, note, diagram)  D26, D28
      div.report-panel#panel-history > details#history-section[open]  D20, D47
      div.report-panel#panel-diagnostics > details.report-diagnostics[open]  D20
    details.failure-clusters, details.background-calls  (the mock leaves these BELOW the panels; see D20)
    button.jump-to-failure, button.back-to-top

  inside each scenario, a section with several diagram types:
    details.example-diagrams[.diagrams-not-seq]
      summary.h4 "Diagrams" (+ the phone gear moved in: button.scenario-diagram-controls-toggle.gear-in-summary)  D34
      div.diagram-toggle.diagram-toggle-tabs[role=tablist] > button.diagram-toggle-btn[role=tab]...  D29
      div.diagram-toggle.diagram-options-bar[.not-seq]   (hidden until body.k-scenario-settings)       D23, D29, D30, D33
        span.details-radio | span.diagram-format | span.diagram-flags
      div.diagram-view ...
```

The mock's stylesheet carries `[hidden] { display: none !important; }` (c-skin.css L495); the product stylesheet has
no such rule. Every `hidden` toggle below depends on it, because rules such as `.header-row { display: flex }`
otherwise beat the browser's own `[hidden]` rule.

---

## 2. Inventory

Each item gives: what the mock does (file, lines, round); kind (STRUCTURE = emitted by ReportGenerator.cs, BEHAVIOUR =
a product script, BOTH); the product code it depends on or overrides; wording; storage and events; risks.

### 2.1 Page frame

**D1. Light and dark theme: boot before first paint, title line, switch, follow the system** (round 5)
- Mock: build.js L276-279 writes a boot script first in the page: reads `localStorage['kronikol.theme']`, else
  `prefers-color-scheme`, and sets `data-theme` on `<html>`. c-dom.r5.js L565-610: wraps `body > h1` in
  `div.title-line`, appends `button.theme-toggle` (two inline SVG icons, `.theme-icon.theme-moon` / `.theme-sun`),
  `aria-label="Dark theme"`, `aria-pressed`, title "Switch to the light theme" / "Switch to the dark theme"; a click
  flips the theme and stores it; while nothing is stored, a `prefers-color-scheme` change listener follows the system.
- Kind: BOTH. STRUCTURE: the boot script in the head raw string of `GenerateHtmlReportCore` (L1518-1575, before the
  `<style>` at L1526), and `div.title-line` plus the button around `<h1>` (L1580). BEHAVIOUR: new script (theme).
- Storage: `kronikol.theme` ("light" / "dark").
- Risks: core `PersistentFilterStateReportTests` L41 asserts `DoesNotContain("localStorage", content)` (added by
  `a87ef53c`, 2026-04-08, "remove localStorage filter persistence"); it fails as soon as any storage key ships. E2E
  `h1` locators: ComponentDiagramPageTests L51, NamesDrawnAsTextTests L50 (text only, unaffected). The Specifications
  report shares `GenerateHtmlReportCore`; the labs page writes its own `<h1>` (LabsReportGenerator.cs); the mock ran on
  neither.

**D2. `kron-c` class on `<html>`** (c-dom.r5.js L674)
- A specificity prefix for c-skin.css (153 uses). Preview-only: OPTION_C_PLAN section 2.2 drops `.kron-c` prefixes.
  Do not port.

### 2.2 Run band (replaces the header row)

**D3. Run band: ring, Test Execution Summary and CI on one row** (c-layout.js L21-114, round 2)
- Mock: reads the summary table back into label/value pairs (skipping the hidden "Kronikol Version" row and the
  `column-header` cells), then builds `section.run-band` (`aria-label="Test execution summary"`):
  `div.run-ring` with a CLONE of the pie SVG; `div.run-zone.run-summary` with `h2.run-title` "Test Execution Summary",
  `span.run-status.run-status-{passed|failed}`, `span.run-when` "yyyy-MM-dd · HH:mm:ss–HH:mm:ss UTC" (the "(UTC)"
  suffixes removed), and `dl.run-stats` of `div.run-stat > dt + dd` for Scenarios, Passed (`run-stat-pass`), Failed
  (`run-stat-fail` only when above 0), Skipped, Bypassed, Features, Duration, then any pair it did not name;
  `div.run-zone.run-ci` with `h2` "CI", the provider's display name (`PROVIDERS` map L36: `GitHubActions` reads
  "GitHub Actions"), stats Build ("#" + number), Branch, Commit (the `<code title=full sha>` cloned), Repository, and
  Run: the Pipeline link cloned as `a.run-link` "View run". Inserts the band before `.header-row`, moves
  `.filtering-box` out of the header row to its own row under the band, and sets `header-row.hidden = true`.
- Kind: STRUCTURE. `GenerateHtmlReportCore` L1666-1708 (summary table, `ci-chart-group`, `ci-metadata`,
  `GeneratePieChartSvg` at L1704, defined L2711). Emit the band from the data; the provider's display name from
  `CiEnvironment` (only `GitHubActions` and `AzureDevOps` exist, CiEnvironment.cs; the mock's map lists six more).
  The header row closes after the filtering box today (L1826-1827), so D3 and D5 change together.
- Wording: "View Run" becomes "View run"; labels lose their colons; "Build #" becomes "Build" with "#" on the value.
- Risks: E2E `header-row` 9/2, `test-execution-summary` 2/2, `ci-metadata` 1/1, `ci-chart-group` 1/1,
  `summary-chart` 4/2. Core `ci-metadata` 7/2, `"CI ("` 6/5, `"Build #"` 2/1, `header-row` 1/1,
  `test-execution-summary` 1/1, `"Start Date:"` 1/1, `"Branch:"` 1/1. The product table has no Bypassed row (the
  mock's Bypassed stat only shows if a row exists), and the summary's Skipped counts `Skipped` only while the features
  table counts Skipped, Bypassed and SkippedAfterFailure (L1588 against L1623): a pre-existing inconsistency the band
  should settle. The Kronikol version, now only in a hidden row, is also in `<meta name="generator">` (L1524).

**D4. Run band layout decided by measurement** (c-layout.js L116-138)
- Mock: measures the natural widths of the summary and CI zones on hidden clones; toggles `.ci-below` on the band
  when ring + summary + CI do not fit; on resize, `DOMContentLoaded`, `fonts.ready`.
- Kind: BEHAVIOUR, new script (or CSS container queries, if the CSS inventory finds a way).

### 2.3 Filter box

**D5. Filter box on its own row; ids** (c-layout.js L112-113; c-round8.js L23; c-round9.js L15)
- Mock: the box is moved under the run band; gets `id="filtering-box"` (for `aria-controls` of the bar's Filters
  button) and `.filters` gets `id="filter-rows"` (for More filters).
- Kind: STRUCTURE. `GenerateHtmlReportCore` L1733-1825, moved out of the header row (L1666, L1826-1827).
- Risks: E2E `filtering-box` 7/3; ReportSectionWidthTests L53 measures `.filtering-box`.

**D6. Row labels without colons; controls wrapped** (c-dom.r5.js L96-104, L127-136; base transform)
- Mock: labels become "Status", "Duration ≥", "Categories", "Dependencies"; each row's controls (everything but its
  label) are moved into `span.fltr-controls`.
- Kind: STRUCTURE. L1758 (status), L1792 (duration), L1797 (dependencies), L1814 (categories).
- Risks: E2E `"Status:"` 2/1, `"Dependencies:"` 1/1. Core `"Status:"` 2/2, `"Categories:"` 1/1;
  DependencyFilterTests builds its own HTML with "Dependencies:" (26 lines, 1 file, not a pin of the generator).

**D7. Duration joins the Status line** (c-dom.r5.js L138-140; TOOLBAR_REDESIGN_PLAN round 8, 2026-08-31)
- Mock: moves `.duration-filters` into `.filter-row`; D18 decides whether Status, Duration and Happy paths share it.
- Kind: STRUCTURE. Emit the duration block (L1775-1793) inside `filter-row` (L1753-1772).
- Risks: E2E `filter-row` 1/1, `percentile-btn` 9/4.

**D8. Happy paths: a three-way control (Show / Only / Hide) over the Happy Path tag** (c-dom.r5.js L142-164,
L248-259, L324-347; labels from round 5)
- Mock: hides `.happy-path-filters` (`hidden`); adds `div.happy-filters` > `span.happy-filters-label` "Happy paths" +
  `span.fltr-controls[role=group][aria-label="Happy paths"]` with three `button.happy-seg[data-hp=off|in|out]`
  labelled Show, Only, Hide (`aria-pressed`). A segment sets the state of the Happy Path chip in the Tags row (D9),
  so there is one filter underneath. `window.filter_happy_paths` is replaced (L325-334): it reads the hidden legacy
  toggle's `.happy-path-active` (which the URL hash and Clear All still drive) into the chip, then filters.
- Kind: BOTH. STRUCTURE L1768-1770. BEHAVIOUR `report-toggle-happy-paths-function.js` (rewrite),
  `report-url-hash-function.js` L78 / L135-138 (`hp=1` means Only), `report-export-function.js` L11-13.
- Wording: "Happy Paths:" / "Happy Paths Only" become "Happy paths" / Show, Only, Hide.
- Risks: E2E `happy-path-toggle` 15/5 (4 click lines in 3 files), `happy-path-active` 6/3, `hp=1` 1/1. Core
  `"Happy Paths Only"` 3/2, `happy-path-toggle` 5/3. Hide is never written to the hash (gap: the August plan's
  hash v2, TOOLBAR_REDESIGN_PLAN section 2.6, is not in the mock).

**D9. Tags row: tri-state chips, Untagged, ANY/ALL** (c-dom.r5.js L106-119, L166-207, L238-283, L305-367;
c-round16.js L1-39; base transform, mode words from rounds 5 and 16)
- Mock: from `fc()` items builds `div.tag-filters` before `.dependency-filters`: `span.tag-filters-label` "Tags",
  `span.fltr-controls` with `button.tag-mode-toggle` (internal `OR`/`AND`, shown as ANY/ALL by D11), one
  `button.tag-toggle[data-tag][data-tag-state=off]` per scenario label (from `data-labels`) plus "Happy Path" for
  `isHappy`, Happy Path first then `localeCompare`, and `button.tag-toggle.tagf-none[data-none=1]` "Untagged". Each
  chip's title is its count ("12 scenarios"). States: `off` / `in` (`.tag-active`, `aria-pressed=true`) / `out`
  (`.tagf-out`, `aria-pressed=mixed`). A capture-phase document `click` cycles off, in, out, off (Alt+click toggles
  straight to out), a capture `contextmenu` goes to out; both `stopPropagation` + `preventDefault`. `filterTags`
  writes the result into each item's `hp` flag, the one the legacy Happy Paths filter used, and calls
  `applyVisibility`.
- Kind: BOTH. STRUCTURE: a new row in L1733-1825; the generator knows `Scenario.Labels` and `IsHappyPath`, so chips
  and counts are emitted, not computed in the page. BEHAVIOUR: new tag filter script, and a flag of its own in
  `report-scenario-feature-map-helper.js` (item flags L17, hidden test L31), not `hp`.
- Wording: "Tags", "Untagged"; titles under D49.
- Risks: no existing test touches tags (`tag-toggle` 0). Feature-level labels are not in `data-labels`, so a feature's
  tags never filter. Tag state is not in the URL hash (gap). The search's `@tag` already reads `data-labels` +
  `data-categories` (report-search-function.js L57-60), so `@tag` and the Tags row can disagree on categories.

**D10. Dependencies: tri-state, "No dependencies", counts in titles** (c-dom.r5.js L209-230, L285-303, L305-367;
base transform)
- Mock: sets each `.dependency-toggle` title to its count; adds a pseudo-chip `button.dependency-toggle.tagf-none`
  `data-dependency="(no interactions)"` "No dependencies" when some scenario has none. Replaces
  `window.filter_dependencies` (L285-303): required chips (`.dependency-active`) combined by `_depMode`, excluded chips
  (`.tagf-out`) remove, the pseudo-chip matches `deps.size === 0 || deps.has('(no interactions)')`. The product's
  inline `onclick="toggle_dependency(this)"` never runs: the capture handler stops the click first.
- Kind: BOTH. STRUCTURE L1795-1803. BEHAVIOUR `report-dependency-filter-function.js` (L10-50 rewritten).
- Risks: E2E `dependency-toggle` 13/6 (4 click lines in 2 files), `dependency-active` 2/1, `toggle_dependency` 2/1,
  `filter_dependencies` 2/1. Exclusions are not in the URL hash. The exclude class is `.tagf-out`, while the August
  plan's compatibility contract (TOOLBAR_REDESIGN_PLAN section 2.0) named a new additive class (`.dependency-exclude`)
  so user stylesheets keep working; decide. `(no interactions)` is also the participant name PlantUmlCreator gives a
  marker-only diagram (PlantUmlCreator.cs L1228): the pseudo-chip should carry its own marker, not that name.

**D11. Mode switches read ANY / ALL; dependencies start on ANY** (c-round16.js L1-39, round 16)
- Mock: a MutationObserver rewrites "AND" to "ALL" and "OR" to "ANY" on `.dep-mode-toggle`, `.tag-mode-toggle` and
  `.cat-mode-toggle` wherever a script writes the mode; the values underneath stay `AND`/`OR`. Sets titles (D49).
  Forces `_depModeDefault = 'OR'`, and `_depMode` too unless the hash names a mode (re-filtering if a chip is on).
- Kind: BOTH. STRUCTURE L1797 and L1814 (button text and title). BEHAVIOUR: every writer of the label maps the mode
  to the word: `report-dependency-filter-function.js` L7, `report-category-filter-function.js` L7,
  `report-export-function.js` L26 and L34, `report-url-hash-function.js` L121 and L126.
- Status: the OR default is ALREADY on `v5` (`9452ddb5`); the mock's override is redundant.
- Risks: E2E FilterModeDefaultsTests asserts the button text "OR"/"AND" 7 times (L35, L45, L56, L73, L83, L86,
  L100). Core ToggleDefaultsMarkupTests L346, L369, L380 pin `>AND</button>` / `>OR</button>`.

**D12. A Clear per tri-state row** (c-round6.js L26-51, round 6)
- Mock: appends `button.group-clear` "Clear" to the Tags and Dependencies rows' controls, hidden unless a chip in the
  row is in or out; a click clicks each set chip back to off (so the chip handlers, hash and Happy paths follow) and
  focuses the first chip. Refreshes on document `click` and `contextmenu`, `kron-filter-change`, `hashchange`.
- Kind: BOTH. STRUCTURE (the button, hidden); BEHAVIOUR (visibility from the filter state, not from document clicks).

**D13. Export buttons drop "Filtered" at narrow widths** (c-dom.r5.js L122-125)
- Mock: wraps "Filtered " in `span.wide-only` (hidden at 600px and below, c-skin.css L406 and L440).
- Kind: STRUCTURE L1735.
- Risks: E2E `"Export Filtered"` 20/10, `export-btn` 22/12; text content is unchanged, but the accessible name
  changes on phones.

**D14. What the filters leave showing; `kron-filter-change`** (c-dom.r5.js L380-446; phone line from round 5)
- Mock: inserts `span.filter-readout[role=status]` before Clear All. A MutationObserver on `#report-content`
  (`style`, `hidden`, subtree), throttled by `requestAnimationFrame`, recounts: features and scenarios shown, a
  parameterized scenario counting one run per distinct `data-row-idx`; writes "All N scenarios, F features" or
  "<b>Filtered</b> n of N scenarios, f of F features", toggles `.is-filtered`, and on a change of the shown set
  dispatches `document` CustomEvent `kron-filter-change` `{ shown, total }`. On phones it wraps
  `.mobile-filter-toggle` in `div.mobile-filter-line` and moves the readout onto that line (matchMedia 768px).
- Kind: BOTH. STRUCTURE: the readout span (L1735), the phone line (L1737), and a `data-runs` count on each scenario
  element so the page need not scan example tables. BEHAVIOUR: compute and announce at the end of `applyVisibility`
  (`report-scenario-feature-map-helper.js` L28-52) instead of observing styles.
- Consumers: D12, D24, D27, D28.

**D15. More filters: the rows fold away on a desktop** (c-round9.js L1-67, round 9)
- Mock: appends `button.more-filters[aria-controls=filter-rows]` (`span.more-filters-label` "More filters",
  `span.more-filters-count`, a chevron SVG) to `.filter-search`; adds `.rows-folded` to the box at start. While folded
  the count shows how many filters in the rows are on (status, dependency in or out, tag in or out, a category other
  than All, one for any duration); `.has-on`, `aria-expanded`, `aria-label` "More filters, N on". The search help
  still opens while folded (`.help-open`, from a MutationObserver on the panel's `style`). Folding and unfolding go
  through `__kronikolNavFilters.reflow` when the box rides in the bar (D24). CSS folds only at 769px and up.
- Kind: BOTH. STRUCTURE L1736 (button) and the folded class at generation. BEHAVIOUR: new script; `toggle_search_help`
  (report-search-function.js L3-6) sets the help class directly.
- Risks: high. On a desktop every row control is out of view until More filters is pressed: E2E clicks on
  `status-toggle` 12 lines in 7 files, `category-toggle` 5/3, `dependency-toggle` 4/2, `happy-path-toggle` 4/3,
  `cat-mode-toggle` 1/1; `percentile-btn` is referenced 9/4.

**D16. Every filter in the search: `dep:`, `duration:`, `is:happy`** (c-round16.js L41-207, round 16)
- Mock: wraps `window.run_search_scenarios`: splits qualifiers out of the input (regex `QRX`, keys `dep`,
  `dependency`, `duration`, `is`; values bare or quoted), tidies the operators left dangling, runs the report's search
  on the rest by swapping `#searchbar`'s value for the call, then restores it (selection kept) and calls
  `update_url_hash`. Each qualifier is a test on an item: `dep:` matches a dependency name containing the value
  (lower-cased, non-alphanumerics dropped; commas for any of several); `duration:` compares the scenario's own time
  (`data-duration-ms` minus `data-warmup-ms`) with `>`, `>=`, `<`, `<=`, `=`, a range `a..b`, units ms/s/m, or the
  report's percentiles `p50..p99` from `.duration-filters[data-p*]`, bare value meaning at least; `is:happy`. `!!`
  negates. Results go into a new item flag `q`; `window.applyVisibility` is wrapped to hide `q` items and re-derive
  rule and feature visibility. A malformed qualifier stays text and marks the bar `.kron-q-bad`
  (`data-q-bad`). Adds three rows (`tr.kron-q-row`) to `.search-help-table` and a sentence
  (`span.kron-q-note`) to `.search-help-note`; changes the placeholder. Re-runs a search opened from a `q=` link.
- Kind: BOTH. STRUCTURE: placeholder, help rows and note (L1736, L1740-1751). BEHAVIOUR:
  `report-search-function.js` `run_search_scenarios` (L32-202) parses qualifiers itself; `fc()` gets a `q` flag and
  `applyVisibility` reads it (report-scenario-feature-map-helper.js L17, L31); `clear_all_filters` clears it.
- Wording: placeholder "Search... (@tag, $status, &&, ||, !!, parentheses)" becomes
  "Search... (@tag, $status, dep:, duration:, &&, ||, !!)"; help rows and note quoted in section 4.
- Mock defect: Clear All empties the bar but never resets `q`, so scenarios a qualifier hid stay hidden after Clear
  All (the wrapper of `clear_all_filters`, c-dom.r5.js L369-378, does not call the search, and the mock's
  `q-test.js` has no Clear All case).
- Risks: E2E AdvancedSearchTests (15 facts), DeepSearchTests (23 facts), `searchbar` 19/7, `search-help` 13/5,
  `run_search_scenarios` 2/1. The August plan (section 2.5) also wanted `dep:none` and quoted `@"tag names"`; the mock
  has neither.

**D17. The deep search chip sits inside the field** (c-round13.js L116-145, round 13)
- Mock: watches for `.kron-deep-chip` (added to `.filter-search` by the search index script) and sets its
  `style.right` from the field's right edge plus 8px, on every show and resize, because More filters (D15) now sits
  after the field and the product's 3em offset put the chip over the buttons.
- Kind: BEHAVIOUR `report-search-index.js` `chipEl` / `showChip*` (L426-462), or CSS once the search line's markup
  is final.
- Risks: E2E `kron-deep-chip` 5/1 (DeepSearchTests).

**D18. Filter and toolbar layout decided by measurement** (c-dom.r5.js L470-536; August round 8)
- Mock: measures Status, Duration and Happy paths on hidden clones (label inline, 28px gaps) and toggles
  `.pair-fits` / `.trio-fits` on `.filter-row`; sets `--kron-nav-h` on `<html>` to the sticky bar's height (for
  anchored scrolls); toggles `.zone-adjacent` on `.diagram-flags` when it sits within 40px of the zone before it on
  the same line. Re-runs on resize, `toggle` and `click` (capture), `DOMContentLoaded`, `load`, `fonts.ready`.
- Kind: BEHAVIOUR, new script (layout). Dead code to leave out: the `.fcnt` scan and `--kron-count-slot`
  (L232-235): no element has class `fcnt`.

**D19. Duration buttons say they leave the warm-up out** (c-round6.js L10-24, round 6)
- Mock: when any `[data-warmup-ms]` is above 0, titles the label "Each scenario's own time, without the first-call
  warm-up it paid", each percentile button "Scenarios taking 1.2 s or more, not counting first-call warm-up (the
  slowest 10%)", and Custom "Scenarios taking at least the seconds you enter, not counting first-call warm-up".
- Kind: STRUCTURE L1792 (the label already has a title there today, with different words).

### 2.4 The report bar, its tabs and panels

**D20. Report tabs and panels** (c-layout.js L141-202, L557-608, round 2)
- Mock: `.toolbar-row` gets `.report-nav`; a `div.report-tabs[role=tablist][aria-label="Report sections"]` is put
  first in it, with `button.report-tab#tab-{key}[role=tab][aria-controls=panel-{key}][data-tab]` for Features (count
  of features, `span.tab-n`), Timeline (if `#scenario-timeline`), Components (if `#component-diagram`), History (if
  `#history-section`), Diagnostics (if `details.report-diagnostics`, count of its list items). A
  `div.report-panels` after the bar holds `div.report-panel#panel-{key}[role=tabpanel][aria-labelledby][data-panel]`,
  one per tab, into which the sections are moved. History and Diagnostics are set open, their summaries made inert
  (`preventDefault`, `tabindex=-1`). `activate(key)`: `hidden` on the other panels, `aria-selected`, roving
  `tabIndex`, `.on-features` on the bar, removes the inline `display:none` the product put on the timeline and
  component sections, renders the component diagram on first open (`_renderDiagramsInContainer`) or redraws it if a
  filter changed it (D28), and dispatches `resize`. Arrow Left/Right, Home and End move between tabs. A capture
  `click` on `.jump-to-failure`, `.history-link` or `.report-diagnostics a[href^="#"]` switches to Features (Full
  view) first; ArrowDown/ArrowUp outside a field switches to Features. The original `.toolbar-left` (the two expand
  buttons and the Scenario Timeline / Component Diagram toggles) becomes `.toolbar-left-retired`, hidden.
- Kind: BOTH. STRUCTURE: `GenerateHtmlReportCore` L1829-1862 (bar), L1925-2037 (failure clusters, diagnostics,
  background calls, history, timeline, component diagram) and L2039 (`#report-content`) wrapped into panels; the
  timeline and component toggles (L1840-1845) and their functions (L1350-1393) go. `HistoryHtml.Section`
  (History/HistoryHtml.cs L139-140) and `RenderReportDiagnostics` (L4344-4370) render open. BEHAVIOUR: new tabs script.
- Configuration: `ReportToggleDefaults.ScenarioTimelineVisible` and `ComponentDiagramVisible` (mutually exclusive
  panels, ReportToggleDefaults.cs L61-66) have no meaning once the sections are tabs, `DiagnosticsOpen` (L87-90) is
  inert inside a tab, and `FeaturesSummaryOpen` (L75-76) belongs to D21. The mock always opens on Features. These are
  public options: a v5 decision for the owner (rename to a start tab, or remove).
- Mock gaps: `details.failure-clusters` and the background calls section are not moved into a panel, so they render
  below all the panels, after the whole feature list, on every tab; the preview's report had neither, so this was never
  seen. A failure-cluster link (`a.failure-cluster-scenario-link`) and a `#sid-` link followed while another tab is
  showing (`hashchange`, report-init-script.js L6-9) do not switch to Features, so they scroll to a hidden scenario.
  The Specifications report (no run data, no durations) and the labs page were never run through the mock.
- Risks: E2E `timeline-toggle` 19/8 (6 click lines in 5 files), `toggle_component_diagram` 13/5, `toggle_timeline`
  4/2, `"Scenario Timeline"` 8/7, `scenario-timeline` 16/7, `component-diagram` 28/9, `history-section` 14/6
  (HistoryExportTests L20 expects it visible; in a closed tab it is not), `report-diagnostics` 3/2, `toolbar-row` 37/10,
  `failure-cluster` 65/3 (placement), `#sid-` 20/5. Core `timeline-toggle` 6/2, `scenario-timeline` 11/4,
  `history-section` 23/6, `report-diagnostics` 24/9.

**D21. The Features tab's tools: Full / Summary, Sort, and the summary table** (c-layout.js L204-336; c-round6.js
L53-69; c-round9.js L108-151; rounds 2, 6, 9)
- Mock: `div.features-tools` in the bar holds `div.toolbar-left.features-tools-line` and, under it, the report's
  `.toolbar-right`. The line, after round 6 and 17, reads: `div.view-switch[role=tablist][aria-label="Features view"]`
  with `button.view-btn[role=tab]` "Full" and "Summary" (`aria-selected`, roving `tabIndex`, Arrow/Home/End; round 9
  turned them from pressed buttons into tabs), the two fold groups (D22), `span.tools-spacer`, Scenario settings (D23),
  `label.sort-group` > `span.sort-label` "Sort" + `select#features-sort.sort-select[aria-label="Sort features"]`, and
  the phone gear (D35). Summary moves `.features-summary-table-wrapper` out of `details.features-summary-details`
  (which is then hidden) into `div.features-table-view` (hidden), beside `#report-content` in `div.features-panel`;
  the bar gets `.features-table-mode`, which hides the report note controls and the expand buttons. The table gains a
  `tr.table-groups` header row ("", Scenarios, Steps, Duration, `th.grp-start`), its group-leading columns are
  renamed "Total", and `.grp-start` marks the first cell of each group. Sort options: Feature A–Z (document order),
  Failures first (column 3), Most scenarios (1), Slowest total (9), Slowest average (10), Longest scenario (11);
  `applySort` reorders the feature cards and the table rows alike and clears the table's own sort marks. A table row
  click opens its feature in the Full view and scrolls to it.
- Kind: BOTH. STRUCTURE: the line in L1829-1862; the summary table from L1595-1664 emitted inside the panel, with the
  group row, "Total" and `grp-start` (headers L1597-1616, cells L1626-1659). BEHAVIOUR: new script (view switch, sort,
  row click); `sort_table` (column clicks) stays.
- Mock defects: the sort columns are fixed numbers that hold only when a report has both step and duration columns
  (L1603-1615 add them conditionally), so without steps the three time sorts read missing cells and do nothing; the
  generator should key columns by name. "Feature A–Z" is document order, and the HTML report writes features in the
  order it is given (L2041, no `OrderBy`; the data files sort by `DisplayName`). Rows are matched to cards by feature
  name, so two features with one name collide. Summary view hides the expand controls through
  `.report-nav.features-table-mode .collapse-expand-all` (c-skin.css L758), but round 17 replaced those buttons
  (D22), so since round 17 the fold groups and Scenario settings stay in view over the table, acting on hidden cards.
- Risks: E2E `features-summary-details` 5/4 (3 click lines in 3 files), `"Features Summary"` 2/2,
  `feature-summary-table` 3/3. Core `"Features Summary"` 1/1, `step-status-header` 2/1, `"Longest"` 10/3, `"Avg"` 3/1.

**D22. Expand and collapse all, as icon pairs** (c-round17.js L31-96, round 17 item 2)
- Mock: replaces each `button.collapse-expand-all` with `span.k-fold-group[role=group][aria-label="Expand or
  collapse all features|scenarios"]`: `span.k-fold-label` ("features" / "scenarios", `aria-hidden`) and two
  `button.k-fold-btn` (`.k-fold-expand`, `.k-fold-collapse`, inline SVG chevrons), titled and labelled "Expand all
  features", "Collapse all features", and the same for scenarios. A button with nothing to do gets
  `aria-disabled=true` (re-checked on every `toggle`, capture, per animation frame). Expanding the scenarios also opens
  their features. Supersedes c-layout.js L291-292 (which moved the old buttons) and the tooltip rule for
  `.collapse-expand-all` (c-round9-tips.js L38-45, now dead).
- Kind: BOTH. STRUCTURE L1833-1835 (and the seeded flip labels: `FeaturesExpanded` / `ScenariosExpanded`, whose doc
  comments promise a seeded button label). BEHAVIOUR `report-collapse-expand-all-function.js` (rewrite).
- Risks: the largest single test change. E2E `collapse-expand-all` 89/23 (78 lines that click, in 22 files),
  `"Expand All Features"` 48/22, `"Expand All Scenarios"` 37/19, `"Collapse All Features"` 1/1,
  `"Collapse All Scenarios"` 1/1, ReportToolbarTests L25-88 (label flip). The shared helper
  `PlaywrightTestBase.ExpandFirstScenarioWithDiagram` (L197-200) clicks both buttons and is called on 214 lines in 62
  files, so it is the place to change first; the 22 files above click the buttons directly as well. Core 2/2.

**D23. Scenario settings: each scenario's options bar starts hidden** (c-round17.js L98-123; c-skin.css L1775;
round 17 item 23)
- Mock: inserts `button.k-flag.k-scenario-settings-toggle` "Scenario settings" (`aria-pressed`, two titles) before
  the Sort group; toggles `body.k-scenario-settings`; CSS hides `details.example-diagrams > .diagram-options-bar`
  without it.
- Kind: BOTH. STRUCTURE (button; bars emitted hidden). BEHAVIOUR: new script.
- Storage: `kronikol.scenarioSettings` ("shown" / "hidden").
- Mock gap: a scenario with only sequence diagrams has no tabs, so D29 never gives its toolbar the
  `diagram-options-bar` class, and its controls stay in view whatever the switch says (in the preview's report, 3 of
  the 167 `diagram-toggle` toolbars had no Activity tab).
- Risks: E2E `SetScenarioState(` 55/10 (DiagramNotePlaywrightBase L85-89 clicks
  `.diagram-toggle .details-radio-btn`), `.diagram-toggle .details-radio-btn` 21/9, `details.scenario .toggle-btn`
  6/2, `.diagram-toggle .toggle-btn` 3/2, `.diagram-toggle .truncate-lines-select` 4/2. If the port hides every
  scenario bar, as the owner asked, each of these needs the switch turned on first.

**D24. The bar carries the filters** (c-round8.js L1-253, round 8 reworked; item 11 of round 17)
- Mock:
  - wraps the tabs in `div.nav-row` and adds `div.nav-filters[role=group][aria-label=Filters]`: `span.nav-readout`
    (`aria-hidden`; `span.nav-readout-long` copies the box readout, `span.nav-readout-short` "Filtered n of N" or
    "All N scenarios"), `button.nav-clear` "Clear All" (shown only while filtered; clicks the box's Clear All), and
    `button.nav-filters-toggle` (funnel SVG + "Filters", `aria-expanded`, `aria-controls=filtering-box`,
    `.is-active` while filtered);
  - `fit()`: `.nav-compact` (short readout), then `.nav-minimal` (no readout) before the tabs would scroll;
  - empty state: `div.filters-empty` (first in the Features panel, hidden) "No scenarios match the filters." with
    `button.filters-empty-clear` "Clear All", which also moves focus to `#searchbar` (round 16, WCAG 2.4.3);
  - riding: Filters moves the box to the top of the bar (`.has-filters`, `.riding`), appends `button.filters-close`
    (X SVG, `aria-label` "Take the filters out of the bar"), focuses the search, caps the box at 60% of the window
    less the bar's rows; X, Esc, a resize to phone width, or the bar coming to rest puts it back;
  - `div.nav-mark` before the bar marks where it pins; `div.nav-float-sentinel` (round 17 item 11) tells when the bar
    floats (`.k-floating`), and the Filters button shows only then;
  - scroll keeping: where the page was when a filter control was used (pointerdown, keydown, input in the bar, box or
    empty state; forgotten on wheel, touchmove or a navigation key) decides, once the result changes, whether the list
    starts right under the bar or stays put; opening and closing keep what was being read under the bar
    (`elementFromPoint` below the bar, scroll anchoring off while the box moves); with the box riding, the panels keep
    at least a window's height (CSS `.report-nav.has-filters ~ .report-panels`);
  - exposes `window.__kronikolNavFilters = { open, close, reflow }` (used by D15 and by the harness's `nav-check.js`).
- Kind: BOTH. STRUCTURE: the row, group, empty state, mark and sentinel. BEHAVIOUR: new script. Desktop only; on a
  phone the bar is static and the box keeps its own Filters toggle.
- Risks: no existing test covers it; any E2E test that scrolls and then uses a filter now runs through `settle()`.

**D25. The bar shows it is pinned** (c-round9.js L69-93, round 9)
- Mock: `.is-pinned` on the bar while `position: sticky` and the mark is above the pin line (scroll, resize);
  squares the bar's corners and gives it a shadow.
- Kind: BEHAVIOUR (new; an IntersectionObserver on the mark would do).

**D26. Component Diagram heading** (c-round9.js L94-102, round 9)
- Mock: puts the text "Component Diagram " and `span.timeline-info` (U+1F6C8, the timeline header's info mark) with a
  long title at the start of `.component-header` (D28), as "Scenario Timeline" heads the timeline.
- Kind: STRUCTURE L2036 (the timeline header at L1987 is the model).
- Note: U+1F6C8 is an emoji code point (round 11 removed emoji fonts from the font stacks after U+25B6 drew as an
  emoji); it already ships in the timeline header (L1990), so it renders however that one does.

### 2.5 Timeline and Components tabs

**D27. Timeline: Filtered or Full, warm-up shown or hidden, ordered with or without it** (c-layout.js L338-450,
spliced before round 4)
- Mock: reads each row's seconds from its bar's `title` ("2.37 s, 2.26 s of it first-call warm-up: ..." or "2.8s"),
  matches a row to scenarios by the label's text, and appends to `.timeline-header` a `div.timeline-controls` of
  `span.tl-seg[role=group]` groups (`span.tl-seg-label` + `button.tl-seg-btn[data-view|data-warm|data-order]`,
  `aria-pressed`): Show Filtered / Full; when any row has warm-up, Warm-up Shown / Hidden and Order With warm-up /
  Without warm-up. Applying: rows of hidden scenarios get `hidden` (Filtered), bars are rescaled to the longest row
  showing, with warm-up hidden a bar and its duration show the time after warm-up and `.timeline-warm-up` is hidden,
  rows are re-sorted. A `div.filtered-note[role=status]` reads "Filtered: this timeline shows the N of M scenarios that
  match the filters." Re-applies on `kron-filter-change` while Filtered.
- Kind: BOTH. STRUCTURE L1975-2020: controls and note in the header, and `data-duration-ms`, `data-warmup-ms`,
  `data-stable-id` on each `.timeline-row` (so nothing is parsed back from titles). BEHAVIOUR: new script.
- Mock defects: the seconds regex cannot read `FormatDurationBadge`'s minute form ("1m 5s", ReportGenerator.cs
  L4315-4327), so a scenario of a minute or more keeps its old width and is left out of the scale; rows are matched by
  display name, so same-named scenarios in two features show together.
- Risks: E2E `timeline-bar` 1/1, `timeline-warm-up` 2/1 (WarmUpMarkTests), ReportToolbarTests L342 (the info
  title).

**D28. Components: Filtered or Full, direction, call summaries** (c-layout.js L452-555, spliced before round 4)
- Mock: wraps `window._preProcessSource` so the component diagram's source is rewritten on its way to the engine:
  the `title` line is always dropped; Filtered (while filtering) drops the nodes whose label (first line, `**`
  removed) is a known dependency no shown scenario lists in `data-dependencies`, and every edge touching them; Left
  to right adds `left to right direction` if absent, Top to bottom removes it; Call summaries Hidden strips edge
  labels and dedupes identical bare edges. The header (`div.timeline-header.component-header`) gets a
  `div.timeline-controls` with Show Filtered / Full, Direction Left to right / Top to bottom, Call summaries Shown /
  Hidden, and a `div.filtered-note` ("Filtered: only the components and connections used by the N scenarios that match
  the filters. The call counts on the connections are for the whole run."). A filter change redraws after 150ms when
  the tab shows, else marks it to redraw on open (D20). Redraw: clears the element, deletes `data-queued`, sets
  `data-plantuml` to the original source and calls `_renderDiagramsInContainer`.
- Kind: BOTH. STRUCTURE L2023-2037 (header, controls, note); the generator can also leave the title out of the
  embedded source and emit the dependency-to-node mapping, instead of the page recovering it with regexes.
  BEHAVIOUR: the component branch of `_preProcessSource` (collapsible-notes-script.js L2406-2408 returns the source
  untouched today) or a new component script; `plantuml-browser-render-script.js` `enqueueElement` (L1479-1503) and
  `_renderDiagramsInContainer` (L1546). Only meaningful under BrowserJs: a component diagram drawn by the report's
  renderer (inline SVG, image, failure, L2029-2035) cannot be redrawn in the page.
- Risks: the regexes (`NODE`, `EDGE`, L467-470) depend on ComponentDiagramGenerator's statement shapes. E2E
  `component-diagram` 28/9, ComponentDiagramPageTests (2 facts), ComponentDiagramDatabaseToggleTests (4 facts, the
  Databases toggle must still leave this diagram alone).

### 2.6 Diagram toolbars (report bar and scenario bars)

**D29. Split each scenario toolbar: the view tabs, then the options** (c-dom.r5.js L69-90; August "W3 split")
- Mock: removes `.diagram-toggle-spacer`; when a toolbar has `.diagram-toggle-btn` tabs, moves them into a new
  `div.diagram-toggle.diagram-toggle-tabs[role=tablist]` (each tab `role=tab`) inserted before it, copies
  `data-layout` to it, and marks the original `.diagram-options-bar`. The report bar is skipped.
- Kind: STRUCTURE. Six emission sites (the builder's comment says five): plain scenario L2310-2320, L2324-2330,
  L2339-2349; `RenderParameterizedGroup` L3360-3385 (with a fourth variant, filters without the Details radio, for a
  group without sequence diagrams), L3389-3395, L3399-3404. `BuildScenarioDiagramToolbar` L1076-1093 (spacer at
  L1081). BEHAVIOUR: `diagram-toggle-layout-script.js` lays out `summary + .diagram-toggle` (L40, L62), which is now
  the tabs bar; `toggle-script.js` L22-43 (`btn.closest('.diagram-toggle')` now finds the tabs bar; its parent is
  still the section, so the view switch keeps working).
- Risks: E2E `diagram-toggle` 54/20, `diagram-toggle-btn` 18/8 (11 click lines in 6 files), `data-layout` 5/1
  (DiagramToggleLayoutTests, 2 facts), `diagram-toggle-spacer` 1/1. Core `diagram-toggle` 20/3,
  `class="diagram-toggle"` 1/1, `diagram-toggle-spacer` 1/1.

**D30. Three zones in every bar: Detail, Format, Toggle** (c-dom.r5.js L53-67, L88-90)
- Mock: in each options bar and in the report bar, the Details radio first, then `span.diagram-format` (format and
  width selects; `.note-font-select` is also listed but was removed in 4.0.0), then `span.diagram-flags` (the toggle
  chips), then the rest. D18 adds `.zone-adjacent` to the flags.
- Kind: STRUCTURE. `BuildScenarioDiagramToolbar` (L1076-1093) and the report bar (L1847-1860).

**D31. Details radio reads as Payloads** (c-dom.r5.js L38-51; label from rounds 10, 11 and 16)
- Mock: `.details-radio-label` "Details:" becomes "Payloads" (round 16, the owner: the boxes hold payloads); buttons
  Expand / Collapse / Truncate become Expanded / Collapsed / Truncated; truncate options read "N LINES"; width options
  are upper-cased (WRAP, WIDE). The product's separate `span.truncate-lines-label` "lines" is then hidden by CSS.
- Kind: STRUCTURE `BuildDetailsRadio` L1028-1044 (drop the "lines" span), width options L1473. Upper case is better
  done with CSS `text-transform` than in the text.
- Risks: core ToggleDefaultsMarkupTests L177-183 and L612 pin whole buttons ending `>Expand</button>`, plus
  `>Truncate</button>` 1/1, `"Details:"` 3/1; E2E `"Wrap"` / `"Wide"` 1/1.

**D32. Toggle chips without "Shown" / "Hidden", with `aria-pressed`** (c-dom.r5.js L22-36; August, locked)
- Mock: stores the bare word in `data-chip-label`, writes it back whenever the report's scripts rewrite the label, and
  keeps `aria-pressed` equal to `data-shown` (a MutationObserver per chip).
- Kind: BOTH. STRUCTURE `BuildFilterToggleButton` L1051-1056. BEHAVIOUR `collapsible-notes-script.js` `syncToggleBtn`
  L2843-2851 writes the bare word and `aria-pressed`.
- Risks: E2E `"Headers Hidden"` 8/4, `"Headers Shown"` 2/2, `"Steps Shown"` 3/2, `"Steps Hidden"` 2/2,
  `"Databases Shown"` 4/1, `"Databases Hidden"` 3/2, `"Assertions Shown"` 1/1; `Shown"` 10/6 and `Hidden"` 14/7 in
  all. Core `Shown</button>` 2/2, `Hidden</button>` 8/3 (ParameterizedGroupRenderTests, ReportToggleTests,
  ToggleDefaultsMarkupTests), `" Shown"` 16/6.

**D33. The options bar is for sequence diagrams only** (c-dom.r5.js L448-465)
- Mock: `.not-seq` on the options bar and `.diagrams-not-seq` on the section while the active tab is not Sequence
  (a MutationObserver on the tabs' classes); CSS hides the bar and the phone gear.
- Kind: BOTH. STRUCTURE: the initial classes from `ResolveDiagramTab`. BEHAVIOUR: set them in `toggle-script.js`'s
  tab handler instead of observing.

**D34. Phones: the view tabs stay, the settings gear moves onto the section's title** (c-dom.r5.js L538-563)
- Mock: on `DOMContentLoaded` and `load`, removes the "U+2699 Diagram Settings" button the product put before each tabs
  bar and undoes `.scenario-diagram-controls` / inline `display` on it; for each options bar, moves its gear into the
  section's `<summary>` as a bare U+2699 (`.gear-in-summary`, `aria-label` and title "Diagram settings",
  `preventDefault` so the summary does not toggle; `.gear-open` while the bar shows), keeping the glyph against the
  product's label rewrites.
- Kind: BEHAVIOUR `report-init-script.js` L33-49 (and the gear could be emitted in the summary).
- Wording: "U+2699 Diagram Settings" / "U+2699 Hide Settings" become U+2699 with the label "Diagram settings".
- Risks: E2E `scenario-diagram-controls-toggle` 6/3, `scenario-diagram-controls` 9/3, `"Diagram Settings"` 1/1;
  MobileResponsiveTests (33 facts) and ViewportSweepTests (11) cover phone layouts.

**D35. Phones: Display settings gear for the report bar** (c-layout.js L293-301)
- Mock: `button.tools-gear` U+2699 (`aria-label` and title "Display settings", `aria-expanded`) toggles
  `.tools-open` on the bar; at 768px and below the report note controls show only with it.
- Kind: BOTH.

**D36. The report-wide note settings are remembered** (c-dom.r5.js L612-672, round 5)
- Mock: after a click on a report-bar radio or a change of a report-bar select, saves Details, line count, every
  report toggle by its `data-toggle` (so Events and Setup too), format and width; on `load` restores them through the
  report's own handlers (`_setTruncateLines`, `_setReportDetails`, `b.click()`, `_setNoteFormat`, `_setNoteWidth`).
- Kind: BEHAVIOUR, new script.
- Storage: `kronikol.reportSettings` (JSON).
- Risks: a stored choice overrides the report's configured `ReportToggleDefaults` on every open, and every report
  served from one origin shares it (local `file://` reports may all share one store, depending on the browser). It
  reverses the 2026-04-08 decision behind PersistentFilterStateReportTests (D1). Owner question.

**D37. Payload wording for the format and width controls** (build.js L62-89, rounds 11, 16, 17 item 3)
- Mock: "Note payload format" becomes "Payload format" (`aria-label` and title), "Note width" becomes "Payload width"
  (`aria-label`, `optgroup`, title "Payload width. Wrap: they wrap at a set width. Wide: they widen to fill the
  diagram."), the note buttons' "Widen this note to fill the diagram" and "Back to the default note width" become
  "Widen this box to fill the diagram" and "Back to the default width", and the format select gets
  `<optgroup label="Payload format">`. The D49 titles then replace both select titles at runtime.
- Kind: BOTH. STRUCTURE L1458-1481 and `BuildNoteAppearanceSelect` L1107-1113. BEHAVIOUR `createNoteButtons`
  (collapsible-notes-script.js L318 on). The comment at L1450-1457 explains why the format select has no optgroup
  (Chromium draws it 15px wider): the owner chose the heading anyway.
- Risks: E2E NoteFormatDropdownTests L58 asserts the `aria-label` "Note payload format"; `"Note width"` 1/1, `optgroup`
  1/1. Core `"Note width"` 3/1, `<optgroup` 1/1.

### 2.7 Notes and diagrams: build-time patches to the report's scripts

These are text patches build.js makes to the 4.26.4 scripts. They may also appear in the script-patch inventory
(plan section 3.3); they are listed so nothing falls between the two. Several anchors no longer exist on `v5`: 4.26.8
moved the compose chain into `composeNoteSource` and the flag reads into `filterShown` (collapsible-notes-script.js
L1711-1725), so build.js's 'compose' and 'replay-events' regexes would not match a `v5` report and the port has to
re-derive those edits.

**D38. Events and Setup toggles** (build.js L91-181; c-round16-notes.js L1-97; c-round16-strips.js L1-79; round 16)
- Mock: two filters beside Assertions, Steps and Databases. Events: `stripEventCalls` is `stripDatabaseCalls` with
  `queue` for `database|collections` (participants declared `queue`: Kafka, Event Hubs, Pub/Sub, Service Bus). Setup:
  `stripSetup` drops the `partition ... Setup` frame (the `SeparateSetup` option), else everything from the first
  statement to the first When step bar, and the participants only that part used; a cut that would leave a block open
  returns the source unchanged. Wiring: `window._eventsVisible` / `_setupVisible` (start true), applied in the
  compose chain, the pre-process (`_preProcessSource`) and the replay that maps drawn notes to source notes;
  `kronExactNoteMap` maps notes by a mark carried through the filters (two notes that read alike, "1 row", cannot be
  confused); handlers `_toggleEvents`, `_toggleSetup`, `_toggleScenarioEvents`, `_toggleScenarioSetup`
  (`kronToggle`, `buildKronQueue`). Buttons `Events Shown` / `Setup Shown` (then chips, D32) are added after the
  Databases button: in the report bar when any diagram has events or setup, in a scenario's bar when one of its
  diagrams does (build.js decides by running both filters over every decompressed source).
- Kind: BOTH. STRUCTURE: gates like `hasDatabaseParticipants` (L1441-1443): a `queue` participant (DependencyPalette.cs
  L85 maps `MessageQueue` to `queue`) and a setup to cut; scenario-level gating means the scenario toolbar can no
  longer be one string per report (L1484-1489). New defaults would need `ReportToggleDefaults` entries and tokens in
  `DiagramContextMenu.GetCollapsibleNotesScript` (L146-158): new public options. BEHAVIOUR
  `collapsible-notes-script.js`: `composeNoteSource` L1715-1725, `filteredToOrigMap` L776-798, `_preProcessSource`
  L2406, beside `buildDatabasesQueue` L2961 and `_toggleDatabases` L2983. The mock keeps a scenario's choice on the
  scenario element (`kronFlag`); the product keeps per-container flags read through `filterShown`.
- Risks: new behaviour, so new tests; existing tests select chips by `data-toggle` (84/25), none by position, so two
  more chips after Databases move nothing they select.

**D39. A collapsed payload that is already whole has no + or ▾** (build.js L134-137; c-round16-notes.js L43-55;
round 16 item 5)
- Mock: `kronCollapsedIsWhole` (one line to show and the preview is that line) suppresses the + and the bottom expand
  button.
- Kind: BEHAVIOUR `createNoteButtons` (collapsible-notes-script.js L324, L464, L498).

**D40. The one-line preview drops a JSON body's braces** (build.js L50-60)
- Mock: `getNotePreview` drops the opening `{` and, when the whole body fits, the closing `}`.
- Kind: BEHAVIOUR `getNotePreview` (collapsible-notes-script.js L97-109).

**D41. Note buttons: packed, drawn triangles, a pair on a truncated note, keep the place** (build.js L247-265;
c-round17-buttons.js L1-410; round 17 items 9, 13, 14, 15)
- Mock (diffed against the 4.26.4 function, `r17-createNoteButtons.js`): every top-right button is created at the
  first slot and moved by `transform` (`packTopRight`), so a hidden format or width button leaves no gap (item 14);
  the triangles are `polygon`s 2px clear of their button (`kTriangle`, item 13) in place of `<text>` U+25B2 / U+25BC;
  a truncated note's bottom holds a collapse and an expand button side by side, each two thirds as wide (item 15);
  `data-note-btn` is now set on every button (`truncate`, `expand-bottom`, `collapse-bottom`, `truncate-bottom`);
  shortening a note from its lower half (bottom buttons, or a double-click low in a tall note) waits for the redraw,
  then scrolls so the note's new bottom is where the click was (`keepPlace`, polls every 50ms for up to 10s, item 9).
  The file ends with the "Fragment note indexing" comment that follows the function in the product, so the swap
  writes that comment twice.
- Kind: BEHAVIOUR `createNoteButtons` (collapsible-notes-script.js L318 on).
- Risks: E2E tests that find these buttons by their `<text>` glyph break: `▲` 20/5, `▼` 10/3, `\u25B2` 4/1,
  `\u25BC` 3/1 outside WikiGifTests (counts include comments), in 7 files (ContinuationNoteInteractivityTests, DiagramNoteLongNoteTests,
  DiagramNotePlaywrightBase L146-153, NoteButtonsAfterHeaderHideTests, NoteExpandArrowHeaderHiddenTests,
  NoteYamlToggleTests, YamlNoteCopyTextTests); they should select `[data-note-btn=...]`. Tests reading a button's
  rect `x` only check it lies inside the note (NoteAppearanceTests L87-88), which still holds. Core
  DiagramContextMenuTests L440-630 pin the function's source (`\\u25BC`, `\\u25B2`, `onTruncate()`, `onContract()`):
  about ten facts.

**D42. Note button tooltips** (c-round9-tips.js L129-143, round 9)
- Mock: adds an SVG `<title>` to a button that has none, by its look: a `▲` glyph "Cut this box back to the line
  limit", `▼` "Show more of this box", two lines (plus) "Open this box in full", one line (minus) "Fold this box to one
  line"; on first `pointerover` / `focusin`.
- Mock defect: since D41 the triangles are polygons, not text, so the four triangle buttons get no tooltip in round 17.
- Kind: BEHAVIOUR `createNoteButtons`: set each title from `data-note-btn` when the button is made.

**D43. Diagram menu: open in a new tab during the click** (build.js L203-226; c-round17.js L1-29; round 17 item 21)
- Mock: `window.__kOpenTab` opens `about:blank` while the click is handled; `__kOpenPng` / `__kOpenSvg` /
  `__kOpenText` write the picture (data URL) or source into it with a title ("Diagram (PNG)", "Diagram (SVG)",
  "Diagram source"); where no tab opened, the old blob URL is tried. Fixes Firefox's popup blocker and opaque
  `blob:` origins of `file://` pages.
- Kind: BEHAVIOUR `context-menu-script.js` L694-747 (nine `window.open(URL.createObjectURL(...))` sites; build.js
  requires at least six of its four patterns to match).
- Risks: ContextMenuExtendedTests L193-218 check the item labels only; nothing tests the tab.

**D44. Flame bars in a palette of eight hues** (build.js L228-245; round 17 item 10)
- Mock: `buildBar` writes `--fh` (one of 212, 262, 188, 338, 152, 32, 290, 8, by the source's hash) in place of
  `background:hsl(hue, 60%, lightness by depth)`; CSS draws the bar from `--fh`.
- Kind: BEHAVIOUR `flame-chart-render-script.js` L5-15, plus CSS.
- Risks: E2E `flame-bar` 15/7; no colour assertions found.

### 2.8 Failure, history and the page as a whole

**D45. Expected and Actual without colons** (c-dom.r5.js L467-468)
- Kind: STRUCTURE `ErrorDiffParser.GenerateDiffHtml` (ErrorDiffParser.cs L98, L103).
- Risks: none pinned (`diff-label` 0 in both suites; "Expected:" in tests is message input).

**D46. Word-by-word expected/actual diff** (c-round16.js L209-262, round 16)
- Status: ALREADY PORTED in 4.26.8 (`ErrorDiffParser.Tokens` / `Common`, the token LCS and the space-between-changes
  rule, ErrorDiffParserTests). Drop the mock script.

**D47. History: a failed run's bar is hatched** (c-round13.js L1-56, round 13, WCAG 1.4.1)
- Mock: in each `svg.history-chart-svg` with `.history-bar-fail` bars, adds `<defs><pattern class="k-fail-hatch"
  id="k-fail-hatch-N">` (6x6, a fail-colour rect and a 2px card-colour stripe at 0.45 opacity) and fills the fail bars
  with it; because the chart stretches (`preserveAspectRatio="none"`), the pattern's transform undoes the stretch,
  read from a bar's `getBBox` against its client rect, again on every ResizeObserver call. Watches the body for charts
  drawn later.
- Kind: BOTH. STRUCTURE `HistoryHtml` (History/HistoryHtml.cs, bar classes L226, chart `<svg>` L251): emit the
  pattern and the fill. BEHAVIOUR only if the chart keeps stretching; a chart drawn at its own aspect ratio needs no
  script.
- Risks: E2E `history-chart-svg` 2/1; HistorySectionTests L70 reads the bars' `<title>`s.

**D48. Focus is never left under the pinned bar** (c-round13.js L58-114; round 13, round 17 item 20)
- Mock: on keyboard `focusin` (focus from a pointer within 1s is left alone, item 20) outside the bar and outside
  anything `position: fixed`, scrolls the control just below the sticky bar or into the window; a control taller than
  the free window that already shows is left alone; for a second after, every scroll re-checks it (a diagram above can
  still change height), until the reader scrolls or focus moves.
- Kind: BEHAVIOUR, new script; with the CSS scroll padding (c-contrast.css, `--kron-nav-h` from D18).

**D49. A tooltip on every control** (c-round9-tips.js L1-162, round 9; rules for Events and Setup from round 16)
- Mock: sets `title` from a rule table on load, again when a tab opens, and on `pointerover` / `focusin` for anything
  drawn later. A rule marked force replaces a title already there; a rule marked live is worked out again each time.
  "Where" words: inside `.report-nav` "every sequence diagram in the report", else "this scenario's sequence
  diagrams". Exposes `window.__kronikolTips`.
- Kind: mostly STRUCTURE: every static title, and the where-words (report bar versus scenario bar is known to the
  builders), are emitted by the C# at the site named in section 3. BEHAVIOUR only for markup the scripts draw: note
  buttons (D42), render-failure "Raw PlantUML" summaries (plantuml-browser-render-script.js L178, L1141, L1292).
- Mock defects: the chip rules never apply, because D9 and D10 already gave every chip a count title and those rules
  are not forced, so the tri-state instructions never show (decide between count and instruction); the History and
  Report diagnostics summaries say "click to show or hide" while D20 makes them inert; Background Steps, Activity
  Diagrams and Flame Chart summaries have no rule; the `.collapse-expand-all` rule is dead after D22; "This step has
  one step inside it: click to show or hide them" says "them" for one.
- Risks: few title assertions: ReportToolbarTests L342, WarmUpMarkTests (3, badge titles, unchanged),
  HistorySparklineTests (2), WholeTestFlowTests (1).

**D50. Debug globals** (c-round8.js L249, c-round9-tips.js L158, c-round16.js L204, L261)
- `__kronikolNavFilters`, `__kronikolTips`, `__kronikolQuery` (`split`), `__kronikolTokenDiff`. The harness checks use
  two: `nav-check.js` reads `__kronikolNavFilters`, `q-test.js` reads `__kronikolQuery`. Keep them as test hooks or
  rewrite those checks before they run against the branch (OPTION_C_PLAN section 2, point 6).

---

## 3. Tooltips and titles

### 3.1 The rule table (c-round9-tips.js L27-118)

F = force (replaces an existing title); L = live. {where} is "every sequence diagram in the report" in the report bar,
"this scenario's sequence diagrams" in a scenario's bar. "Today" is the product's current title.

| # | Selector | Title | F/L | Emit at | Today |
|---|---|---|---|---|---|
| 1 | `.report-tab[data-tab=features]` | The features and their scenarios, with the filters applied | | D20 tabs (new) | |
| 2 | `.report-tab[data-tab=timeline]` | Every scenario as a bar, slowest first, with the first-call warm-up it paid | | D20 | |
| 3 | `.report-tab[data-tab=components]` | The services, databases and queues the scenarios called, drawn as one diagram | | D20 | |
| 4 | `.report-tab[data-tab=history]` | This run against the earlier runs on the same branch: what is still failing, new, slower or gone | | D20 | |
| 5 | `.report-tab[data-tab=diagnostics]` | What Kronikol noticed while building this report, such as calls it could not put under a scenario | | D20 | |
| 6 | `.nav-readout` | The scenarios and features the filters leave showing | | D24 | |
| 7 | `.nav-clear` | Clear every filter: the search, statuses, duration, happy paths, tags and dependencies | F | D24 | (c-round8: "Clear every filter") |
| 8 | `.view-btn` | Summary: "One table row per feature, with its counts and times"; Full: "Each feature as a card with its scenarios" | | D21 | |
| 9 | `.sort-select` | The order the features are listed in | | D21 | |
| 10 | `.collapse-expand-all` | "Open every feature; the scenarios inside stay as they are" / "Close every feature" / "Open every scenario, with its steps and diagrams (a closed feature still hides its own)" / "Close every scenario" | F L | dead after D22 | |
| 11 | `.details-radio-btn[data-state=expanded]` | The payloads in {where} show in full | F | `BuildDetailsRadio` L1039 | |
| 12 | `.details-radio-btn[data-state=collapsed]` | The payloads in {where} fold to one line each; a box's + button opens it | F | L1040 | |
| 13 | `.details-radio-btn[data-state=truncated]` | Payloads in {where} longer than the line limit beside this are cut at that many lines (the default) | F | L1041 | |
| 14 | `.details-radio-label` | How the payloads in {where} are shown (the boxes beside the arrows, holding each request, response and message, with its headers): whole, folded to one line, or cut at a line limit | | L1038 | |
| 15 | `.truncate-lines-select` | How many lines a cut payload box keeps in {where} (with Truncated picked) | F | L1042 | |
| 16 | `.toggle-btn[data-toggle=headers]` | Show or hide the HTTP headers in the payloads of {where} | F | `BuildFilterToggleButton` L1055 | |
| 17 | `.toggle-btn[data-toggle=assertions]` | Show or hide the boxes with the test's assertions in {where} | F | L1055 | |
| 18 | `.toggle-btn[data-toggle=steps]` | Show or hide the bars marking where each step starts in {where} | F | L1055 | |
| 19 | `.toggle-btn[data-toggle=databases]` | Show or hide the databases and their calls in {where} | F | L1055 | |
| 20 | `.toggle-btn[data-toggle=events]` | Show or hide the messages to and from message brokers (Kafka, Event Hubs, Pub/Sub, Service Bus) and their payloads in {where} | F | D38 | |
| 21 | `.toggle-btn[data-toggle=setup]` | Show or hide the setup in {where}: everything before the first When step, or the Setup section where the report keeps setup apart | F | D38 | |
| 22 | `.note-format-select` | How the payloads in {where} are written: JSON as captured, or the same data as YAML | F | L1461, L1464 | Note payload format |
| 23 | `.note-width-select` | How wide the payloads are in {where}. Wrap: they wrap at a set width. Wide: they widen to fill the diagram | F | L1474-1480 | Note width. Wrap: notes wrap at the report's note width. Wide: notes widen to fill the diagram. |
| 24 | Clear All (`.export-btn[onclick*=clear_all_filters]`) | Clear every filter: the search, statuses, duration, happy paths, tags and dependencies | | L1735 | |
| 25 | Export HTML | Download a copy of this report holding only the features the filters show | | L1735 | |
| 26 | Export CSV | Download the scenarios the filters show as a CSV file: feature, scenario, status and duration | | L1735 | |
| 27 | `.filter-readout` | The scenarios and features the filters leave showing | | D14 | |
| 28 | `#searchbar` | Search names, steps, tags and diagram text. Combine words with && (and), \|\| (or) and !! (not); @tag and $status pick by tag or status, dep:, duration: and is:happy by dependency, time and happy path; ? shows the syntax | | L1736 | |
| 29 | `.search-help-toggle` | How to write a search: words, "phrases", @tags, $statuses, dep:, duration:, is:happy, &&, \|\|, !! and parentheses | F | L1736 | Search syntax help |
| 30 | `.status-toggle[data-status]` | Show only the scenarios that {passed / failed / were skipped / were bypassed}; pick more statuses to show them together | | L1763 | |
| 31 | `#duration-threshold` | Seconds: scenarios taking at least this long, not counting first-call warm-up | | L1792 | |
| 32 | `.happy-seg[data-hp]` | off: "Happy-path scenarios and the rest"; in: "Only the scenarios tagged as happy paths"; out: "Leave out the scenarios tagged as happy paths" | | D8 | |
| 33 | `.tag-mode-toggle` | OR: scenarios with any of the picked tags. AND: scenarios with all of them. Click to switch | | D9 (overridden by D11's) | |
| 34 | `.tag-toggle[data-none]` | Click: only scenarios with no tags. Click again: leave them out. Once more: off | | D9 (never applied: count title) | |
| 35 | `.tag-toggle` | Click: only scenarios tagged {name}. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once | | D9 (never applied) | |
| 36 | `.dependency-toggle` | Click: only scenarios that call {name}. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once | | L1800 (never applied) | |
| 37 | `.group-clear` | Clear the tag filters / Clear the dependency filters / Clear these filters | | D12 | (c-round6 sets the same) |
| 38 | `.filters-empty-clear` | Clear every filter | | D24 | |
| 39 | `.mobile-filter-toggle` | Show or hide the filters | | L1737 | |
| 40 | `.tl-seg-btn[data-view]` | Timeline: full "Every scenario in the run, whatever the filters", filtered "Only the scenarios the filters show". Components: full "Every component and call in the run, whatever the filters", filtered "Only the components and calls of the scenarios the filters show" | | D27, D28 | |
| 41 | `.tl-seg-btn[data-warm]` | shown: "Bars and times include the first-call warm-up each scenario paid, marked in its bar"; hidden: "Bars and times leave the warm-up out: each scenario's own time" | | D27 | |
| 42 | `.tl-seg-btn[data-order]` | with: "Slowest first, counting the warm-up"; without: "Slowest first by each scenario's own time, warm-up left out" | | D27 | |
| 43 | `.tl-seg-btn[data-dir]` | lr: "Draw the diagram from left to right"; tb: "Draw the diagram from top to bottom" | | D28 | |
| 44 | `.tl-seg-btn[data-calls]` | shown: "Show the call counts and methods on each connection"; hidden: "Hide the call counts and methods on the connections" | | D28 | |
| 45 | `.diagram-toggle-btn` | Sequence: "The calls between the test and the services, in order"; Activity: "The steps and the calls each made, as an activity diagram"; Flame: "Where the time went, call by call, as nested bars" | | L2312-2316, L2341-2342, L3363-3367, L3401-3402 | |
| 46 | `summary.h4` | Steps: "This scenario's steps, with how long each took"; Diagrams: "This scenario's diagrams"; Sequence Diagrams: "The sequence diagrams of this part of the scenario"; Failure Result: "Why the scenario failed: the error, with the expected and actual values" | | L2244, L2310, L2324, L3304, L3360, L3389, `RenderStepsList` L3650 | |
| 47 | `details.step-collapsible > summary` | This step has one step inside it / This step has N steps inside it: click to show or hide them | | `RenderStep` L3731 | |
| 48 | `a.step-attachment` | Open {name}, a file this step attached | | L2277, L3292, L3881 | |
| 49 | `summary` (Raw PlantUML, History, Report diagnostics) | "The PlantUML source this diagram was drawn from"; "This run against the earlier runs on the same branch: click to show or hide the details"; "What Kronikol noticed while building this report: click to show or hide the list" | | render script L178/L1141/L1292; HistoryHtml L140; L4357 | |
| 50 | `a.history-link` | Go to this scenario on the Features tab | | HistoryHtml L127, L133 | |
| 51 | `.jump-to-failure` | Go to the next failed scenario; the count says which one of how many | | L2453 | |
| 52 | `.back-to-top` | Back to the top of the report | | L2457 | |
| 53 | `.run-link` | Open the CI run this report came from | | D3 | |

### 3.2 Titles set elsewhere in the mock

| Element | Title (and aria-label) | Source |
|---|---|---|
| tag and dependency chips, Untagged, No dependencies | "N scenario" / "N scenarios" | c-dom.r5.js L117-119 |
| `.tag-mode-toggle` | "OR: a scenario with any of the chosen tags. AND: a scenario with every chosen tag." (then D11's) | c-dom.r5.js L187 |
| `.dep-mode-toggle` | "ANY: scenarios that call any of the chosen dependencies. ALL: scenarios that call every one of them. Click to switch." | c-round16.js L30 |
| `.tag-mode-toggle` | "ANY: scenarios with any of the chosen tags. ALL: scenarios with every one of them. Click to switch." | c-round16.js L33 |
| `.cat-mode-toggle` | "ANY: scenarios in any of the chosen categories. ALL: scenarios in every one of them. Click to switch." | c-round16.js L35 |
| `.theme-toggle` | "Switch to the light theme" / "Switch to the dark theme"; aria-label "Dark theme" | c-dom.r5.js L577, L602 |
| phone section gear | "Diagram settings" (aria-label too) | c-dom.r5.js L552-553 |
| `.tools-gear` | "Display settings" (aria-label too) | c-layout.js L294-295 |
| `.duration-filters-label` | "Each scenario's own time, without the first-call warm-up it paid" | c-round6.js L16 |
| `.percentile-btn` | "Scenarios taking {t} or more, not counting first-call warm-up ({share})"; Custom: "Scenarios taking at least the seconds you enter, not counting first-call warm-up" | c-round6.js L19-22 |
| `.nav-filters-toggle` | "Show the filter box in this bar, so it stays in view as you scroll" / "Put the filter box back above the bar" | c-round8.js L46, L180, L206 |
| `.filters-close` | "Put the filter box back above the bar (Esc)"; aria-label "Take the filters out of the bar" | c-round8.js L165-166 |
| `.more-filters` | "Fold the filters away" / "One filter here is on: show them" / "N filters here are on: show them" / "Status, duration, happy paths, tags and dependencies"; aria-label "More filters" or "More filters, N on" | c-round9.js L42-43 |
| Components info mark | "The Component Diagram shows the services, databases and queues the scenarios called, and the calls between them. Each connection is labelled with the calls made along it and how many tests made them. Filtered draws only what the scenarios the filters show used; Full draws the whole run. Use it to see what a set of tests depends on." | c-round9.js L98 |
| `.k-fold-btn` | "Expand all features", "Collapse all features", "Expand all scenarios", "Collapse all scenarios" (aria-label too) | c-round17.js L80-82 |
| `.k-scenario-settings-toggle` | "Hide each scenario's payload and diagram settings (the bar at the top sets them for every scenario)" / "Show payload and diagram settings on each scenario, to set one apart from the bar at the top" | c-round17.js L114-115 |
| note width button (SVG) | "Widen this box to fill the diagram" / "Back to the default width" | c-round17-buttons.js L190-193 |
| note format button (SVG) | "Show payload as JSON" / "Show payload as YAML" (unchanged) | c-round17-buttons.js L168 |
| note buttons (SVG, D42) | "Cut this box back to the line limit", "Show more of this box", "Open this box in full", "Fold this box to one line" | c-round9-tips.js L135-138 |

---

## 4. Other wording changes (labels, text, placeholders)

| Where | Today | Option C | Item |
|---|---|---|---|
| Details radio label | Details: | Payloads | D31 |
| Details buttons | Expand / Collapse / Truncate | Expanded / Collapsed / Truncated | D31 |
| Truncate options; "lines" span | 40 ... + "lines" | 40 LINES ... (span hidden) | D31 |
| Width options | Wrap / Wide | WRAP / WIDE (upper case) | D31 |
| Toggle chips | Headers Shown / Hidden, ... | Headers, Assertions, Steps, Databases, Events, Setup | D32, D38 |
| Format select | aria-label/title "Note payload format", no optgroup | "Payload format", optgroup "Payload format" | D37 |
| Width select | "Note width" (aria-label, optgroup) | "Payload width" | D37 |
| Filter labels | Status: / Duration ≥: / Dependencies: / Categories: / Happy Paths: | Status / Duration ≥ / Dependencies / Categories / Happy paths | D6, D8 |
| Happy paths | Happy Paths Only (toggle) | Show / Only / Hide | D8 |
| Tags row | (none) | Tags, chip per tag, Untagged | D9 |
| Dependencies | (none) | No dependencies | D10 |
| Mode buttons | AND / OR | ALL / ANY | D11 |
| Row clear | (none) | Clear | D12 |
| Export buttons (phones) | Export Filtered HTML / CSV | Export HTML / CSV | D13 |
| Readout | (none) | "All N scenarios, F features" / "Filtered n of N scenarios, f of F features"; short "Filtered n of N" | D14, D24 |
| Search placeholder | Search... (@tag, $status, &&, \|\|, !!, parentheses) | Search... (@tag, $status, dep:, duration:, &&, \|\|, !!) | D16 |
| Search help rows | (8 rows) | + `dep:name` "Scenarios that call a dependency: part of its name is enough; commas for any of several" (`dep:cow`, `dep:"cow service"`, `dep:cow,goat`); `duration:>2s` "Scenarios by their own time: >, >=, <, <= or a range; ms, s or m; p50, p90, p95, p99 are this report's percentiles" (`duration:>2s`, `duration:<500ms`, `duration:>p90`, `duration:1s..5s`); `is:happy` "Happy paths" (`is:happy`, `!!is:happy`) | D16 |
| Search help note | ... | + " dep:, duration: and is: always narrow the search: they combine with the rest of it by AND, and can be negated with !!." | D16 |
| More filters | (none) | More filters (+ count) | D15 |
| Summary band | Test Execution Summary table; "CI (GitHubActions)"; "Build #"; "Pipeline: View Run" | Test Execution Summary, status pill, "date · start–end UTC"; "CI" + "GitHub Actions"; "Build #n"; "Run: View run" | D3 |
| Report sections | Scenario Timeline / Component Diagram buttons; Features Summary disclosure; History / Report diagnostics disclosures | tabs Features, Timeline, Components, History, Diagnostics | D20 |
| Features tools | Expand All Features / Collapse All ... | Full / Summary; "features" and "scenarios" icon pairs; Scenario settings; Sort | D21-D23 |
| Sort options | (none) | Feature A–Z, Failures first, Most scenarios, Slowest total, Slowest average, Longest scenario | D21 |
| Summary table headings | Scenarios / Passed / Failed / Skipped / Steps / ... / Duration / Avg / Longest | group row Scenarios, Steps, Duration; Total in place of Scenarios, Steps, Duration | D21 |
| Timeline controls | (none) | Show Filtered / Full; Warm-up Shown / Hidden; Order With warm-up / Without warm-up; filtered note | D27 |
| Component controls | (none) | Component Diagram heading; Show; Direction Left to right / Top to bottom; Call summaries Shown / Hidden; filtered note; the diagram's own title line left out | D26, D28 |
| Empty result | (none) | No scenarios match the filters. + Clear All | D24 |
| Phone gear | U+2699 Diagram Settings / Hide Settings | U+2699 alone, labelled Diagram settings | D34 |
| Diff labels | Expected: / Actual: | Expected / Actual | D45 |

---

## 5. Test impact at a glance

| Area | Biggest E2E exposure | Core exposure |
|---|---|---|
| D22 expand/collapse all | `collapse-expand-all` 89/23 (78 click lines); helper `PlaywrightTestBase` L197-200 | 2/2 |
| D29-D33 scenario toolbars | `diagram-toggle` 54/20, `details-radio-btn` 62/21, `toggle-btn` 95/28 | `diagram-toggle` 20/3, `toggle-btn` 24/6 |
| D23 scenario bars hidden | `SetScenarioState(` 55/10, `.diagram-toggle .details-radio-btn` 21/9 | |
| D41 note buttons | glyph lookups in 7 files; `data-note-btn` 118/18 (still valid) | DiagramContextMenuTests about 10 facts |
| D20 tabs and panels | `timeline-toggle` 19/8, `component-diagram` 28/9, `history-section` 14/6, `toolbar-row` 37/10 | `history-section` 23/6, `report-diagnostics` 24/9 |
| D15 folded rows | clicks on status 12 lines/7 files, category 5/3, dependency 4/2, happy path 4/3 | |
| D32 chip labels | `Shown"` 10/6, `Hidden"` 14/7 | `Hidden</button>` 8/3 |
| D11 ANY/ALL | FilterModeDefaultsTests 7 assertions | ToggleDefaultsMarkupTests 3 |
| D3 run band | `header-row` 9/2 | `ci-metadata` 7/2, `"CI ("` 6/5 |
| D1, D36 storage | | PersistentFilterStateReportTests L41 |
| Whole-page suites to re-check | ReportToolbarTests (18 facts), ToolbarStylingTests (11), MobileResponsiveTests (33), ViewportSweepTests (11), ToggleDefaultsTests (20), ReportSectionWidthTests (3), WikiScreenshotTests (9) and WikiGifTests (18, wiki media to re-record) | ToggleDefaultsMarkupTests |

---

## 6. Mock defects and gaps found while reading

1. Clear All leaves scenarios hidden by a `dep:` / `duration:` / `is:` search (D16).
2. Round 17's drawn triangles lost their round 9 tooltips (D41, D42).
3. Chip count titles block the chips' instruction tooltips (D9, D10, D49).
4. A sequence-only scenario's toolbar is never hidden by Scenario settings and keeps the old phone gear (D23, D34).
5. Timeline rows of a minute or more are not rescaled; rows match scenarios by name (D27).
6. Summary sorts use fixed column numbers; "Feature A–Z" is document order (D21).
7. Failure clusters and background calls end up below all panels; cluster links and `#sid-` hash changes do not
   switch to the Features tab (D20).
8. History and Diagnostics summary titles say "click to show or hide" on summaries D20 made inert (D49).
9. Tri-state state (tags, exclusions, Hide, tag mode) is not in the URL hash, against TOOLBAR_REDESIGN_PLAN section
   2.6 (D8-D10).
10. Categories are not tri-state and were never exercised (the preview's report had no Categories row), against
    TOOLBAR_REDESIGN_PLAN section 2.4 (D6, D11).
11. Remembered settings override configured toggle defaults and are shared across reports of one origin (D36).
12. Dead code: `.fcnt` / `--kron-count-slot`, `.note-font-select`, the `.collapse-expand-all` tooltip rule (D18, D30,
    D49).
13. build.js anchors written for 4.26.4 do not match `v5` (section 2.7).
14. The note-button swap duplicates the "Fragment note indexing" comment (D41).
15. The harness copy lacks c-layout.js, c-round16-notes.js and c-round16-strips.js (section 0.4).
16. The dependency pseudo-chip reuses the marker participant name `(no interactions)` (D10).
17. Since round 17, Summary view no longer hides the expand controls: the rule still names `.collapse-expand-all`
    (D21, D22).

---

## 7. Order of work

Each step lists what it needs from earlier steps.

1. **Stylesheet foundation** (CSS inventory): tokens, `[hidden] { display: none !important; }`, sticky bar. Every
   later item hides things with `hidden` and relies on the sticky `.report-nav`.
2. **D1 theme** (head boot script, title line, switch). Independent; settles the storage question (D1, D36,
   PersistentFilterStateReportTests) early, because D23 and D36 store too.
3. **D3, D4, D5 run band and the filter box's own row.** They change together: the header row closes after the
   filter box today (L1826-1827).
4. **D20 bar, tabs and panels**, with the owner's answers on the toggle defaults and on where failure clusters and
   background calls go. Everything in the bar and every tab's content (D21-D28, D48) needs it.
5. **D21, D22, D35 Features tools line, D30 zones of the report bar** inside the bar. D22 first among the test edits:
   the helper `ExpandFirstScenarioWithDiagram` is called from 62 files, and 22 files also click the buttons directly.
6. **Filter pipeline** before the rows that use it: item flags of their own for tags and qualifiers in `fc()` and
   `applyVisibility`, `clear_all_filters`, URL hash v2 (owner), and **D14** readout with its event (D12, D24, D27, D28
   consume it).
7. **Filter rows:** D6, D7, D13, D19 (markup only), then D8, D9, D10, D11, D12 (tri-state; Happy paths depends on the
   Happy Path tag chip of D9), then D16 (qualifiers and help) and D17 (chip placement, after D15 puts a button on the
   search line).
8. **D15 More filters and D24 bar filters, D25 pinned:** need the bar (step 4), the readout (step 6) and each other
   (`reflow`).
9. **D18 measured layout** once the filter rows and the zones are final.
10. **Scenario toolbars:** D29 split (six sites), D30, D31, D32 (with `syncToggleBtn`), D33, D34, D37, updating
    `diagram-toggle-layout-script.js` and `toggle-script.js`; then **D23** Scenario settings (after the split exists,
    and covering sequence-only toolbars too).
11. **Note script:** D38 Events and Setup (needs the chip form and per-scenario toolbar gating), D39, D40, D41, D42.
    Re-derive the 4.26.4 patches against `composeNoteSource` / `filterShown`.
12. **D36 remembered settings**, if the owner keeps it, once every report-bar control is final.
13. **Tab contents:** D26, D27, D28 (need D20 and the D14 event).
14. **Remaining items:** D45 diff labels, D47 history hatch, D43 open in a tab, D44 flame palette, D48 focus (needs the
    sticky bar and `--kron-nav-h`).
15. **D49 titles** are best emitted with each item as it is built; finish with an audit against section 3, and settle
    D50 before running the harness checks against the branch.

---

## 8. Preview-only: do not port

- build.js: removing the report's `<!doctype>`, `<html>`, `<head>` and `<body>` tags; the page title "Option C Report
  Preview"; the `div.kron-preview-note` banner (the BreakfastProvider run and its five relabelled CI runs, #318 to
  #322); the `ENGINE` / `ENGINE_LABEL` swap (engine text in a Blob URL, recomputed SRI, the label naming PlantUML
  master on Wasm GC); the `.local.html` test skeleton and its inline base styles; injecting the stylesheet as
  `<style id="kronikol-c">` and the scripts as `kronikol-c-dom` / `kronikol-c-layout`; the "must be found" anchor
  checks.
- The theme boot script itself is NOT preview-only (D1); only its placement before the report's head is.
- `kron-c` on `<html>` (D2), and every `.kron-c` selector prefix.
- The load-time move-and-hide technique: keeping `.header-row`, `.happy-path-filters`, `.toolbar-left-retired` and
  `details.features-summary-details` hidden in the page for the old scripts; cloning the pie SVG; reading the summary
  and CI tables, the timeline bar titles and feature names back out of the page; the MutationObservers that put the
  bare chip words, ANY / ALL and the gear glyph back after the report's scripts rewrite them; the
  `run_search_scenarios` wrapper that swaps the bar's value, and the second search for `q=` links; the
  `_depModeDefault = 'OR'` override (already on `v5`); the title pass on `pointerover` / `focusin`.
- c-round16.js token diff (D46, in the product since 4.26.8).
- Dead code listed in section 6, item 12.
- `c-layout-tabs.js.part` and `splice-tabs.py` (a one-off splice into c-layout.js); `c-diagram-look.js` here (the
  diagram-pass inventory decides what of it ports).
- Debug globals, unless kept as test hooks (D50).
