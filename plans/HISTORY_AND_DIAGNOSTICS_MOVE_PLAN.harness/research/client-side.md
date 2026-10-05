# Research A: client-side and resource inventory for moving history UI and Report diagnostics out of TestRunReport.html

Repo: C:\Code\Kronikol at 425d9bad (main, clean). Read only; nothing in the repo was modified.
Scope: src/Kronikol/Reports/*.js, *.css, SearchIndex/**, the PlantUML scripts, every EmbeddedResource in
src/Kronikol/Kronikol.csproj (lines 34-50), plus who in tests/, tools/, plans/, docs and the wiki depends on what.

## TL;DR

- **Client code for history is small and self-contained.** It is 4 JS files with about 1.3 KB between them,
  and 2 CSS blocks of about 6.1 KB. **The Report diagnostics section has no client code at all**, only CSS.
  Nothing in toolbars, legends, header stats, keyboard nav, collapse-all, init, toggles or storage knows
  about either feature.
- **All of it ships in every report, whatever the run.** The whole stylesheet and every script are
  embedded unconditionally (ReportGenerator.cs:1306, 1403-1441). That covers reports with no ledger, and
  Specifications.html too, which goes through the same head. Removing the history and diagnostics CSS and
  the verdict JS shrinks every HTML report, not just runs that have history.
- **What `$flaky` does after removal.** Reverting the verdict branch leaves `$<anything>` as a `status`
  token that matches nothing. It is never plain text and never a parse error. Every scenario is hidden,
  silently, because the report has no "no matches" message. A report with no ledger already behaves this
  way.
- **The deep-search index never carried verdicts.** Neither the C# index nor `data-search` holds them.
  Verdicts reach the deep-search worker only as a per-item field in its `init` message.
- **Kronikol4J parity.** An exact revert of `advanced-search.js` and `report-scenario-feature-map-helper.js`
  to their pre-3.11.0 bytes makes them byte-identical to Kronikol4J's committed copies (blobs 908e62c2 and
  bb84c91c). The stylesheet does not regain parity: K4J's copy equals .NET's as of 7b5a23a2 (2026-06-21).
- **Linking from a separate document already works.** The new document can link to
  `TestRunReport.html#sid-<stableId>`. The report already resolves that on load (`parse_url_hash` then
  `reveal_url_anchor`), and StableIdDeepLinkTests covers it with 10 E2E facts. TestRunReport.html needs no
  new client code. `kronikol query` already builds the same link (QueryCommand.Narrative.cs:758-762).
- **Pre-existing defects found:**
  - Two tests pass only because the stylesheet contains the text they look for.
  - Export Filtered CSV's Scenario column includes the pill text, the badges and the button glyphs.
  See section 8.

---

## 0. How the page is assembled (why everything ships unconditionally)

- `ReportGenerator.cs:1306`: `var combinedStylesheet = Stylesheets.HtmlReportStyleSheet + "\n";`.
  Stylesheets.cs:10-19 reads the embedded `stylesheets.css` verbatim. There is no minification and no
  conditional stripping.
- `ReportGenerator.cs:1403-1441`: one `<style>` block holds the base sheet plus the component sheets. One
  `<script>` block holds, in order:
  1. decompressHelper
  2. advancedSearchScript (1414)
  3. scenarioFeatureMapHelper (1415)
  4. toggleHappyPaths
  5. searchFunction (1417)
  6. searchIndexClientScript (1418)
  7. the filters
  8. exportFunction (1435)
  9. persistentFilterFunction
  10. urlHashFunction (1437)
  11. keyboardNavigationFunction
  12. initScript (1439)

  All of these are unconditional. The comment at 1190-1192 says report-search-index.js is "always
  included; it no-ops when the kron-search-index blob is absent".
- Specifications.html is written through `GenerateHtmlReport` (ReportGenerator.cs:483) into the same
  `GenerateHtmlReportCore`. It therefore also carries the history and diagnostics CSS and the verdict JS,
  and never uses either.
- HistoryHtmlTests.cs:199-200 says so explicitly: "The stylesheet and the search script name these
  classes and attributes whether or not the run had history; what must be absent is the markup."

Context: the 3.11.0 commit (c9c3b068) said "a report with no ledger is byte for byte what 3.10.0 wrote".
That was true of the body only. In the same commit the head grew by 4,255 B of CSS and about 1.3 KB of JS
for every report.

---

## 1. CSS inventory

All of it is in `src/Kronikol/Reports/stylesheets.css`. The working copy is CRLF; git stores it as LF
(`i/lf w/crlf`). The committed file is 61,425 B; the working copy is 63,480 B.

### 1a. Report diagnostics (diagnostics-only), lines 1261-1314 (+ blank line 1315)

| Lines | Selector | Notes |
|---|---|---|
| 1261 | `/* Report diagnostics (capture health, skipped lines, render failures) */` | Comment. **This is the only "Report diagnostics" text in a default report** (see 8a) |
| 1262-1268 | `.report-diagnostics` | Amber-bordered box |
| 1269-1275 | `.report-diagnostics > summary` | |
| 1276-1281 | `.report-diagnostics-list` | |
| 1282-1286 | `.report-diagnostics-list li` | Has its own `overflow-wrap: anywhere` |
| 1287-1289 | `.report-diagnostics-list li:last-child` | |
| 1290-1300 | `.report-diagnostic-kind` | Base pill (grey). Every kind without its own rule uses it: MalformedLine, OptionNotApplied, BackgroundCalls, StepAttributionMismatch, ReportRotationFailed, Other… |
| 1301-1304 | `.report-diagnostic-kind-capturedegraded` | Amber |
| 1305-1310 | `.report-diagnostic-kind-renderfailure, -outputfailure, -attachmentfailure` | Red |
| 1311-1314 | `.report-diagnostic-scenario` | `[scenarioId]` as plain text, not a link |

Size: **1,784 B LF / 1,838 B CRLF** (1,785 / 1,840 with the trailing blank line).
Provenance: a87b8183 ("Capture health reaches the report…"); no later commit touched these rules.

### 1b. Cross-run history (history-only), lines 1316-1451 (+ blank line 1452)

| Lines | Selector |
|---|---|
| 1316 | comment `/* Cross-run history: the sparkline and verdict pill beside the duration badge, the History section */` |
| 1317-1326 | `.history-sparkline` (base grey `background`; the per-run stops come from the inline `style` HistoryHtml.cs:71 writes) |
| 1327-1337 | `.history-verdict` (base pill) |
| 1338-1341 | `.history-verdict-broke, -failing, -always-failing` (red) |
| 1342-1345 | `.history-verdict-fixed` |
| 1346-1349 | `.history-verdict-flaky` |
| 1350-1353 | `.history-verdict-new` |
| 1354-1357 | `.history-verdict-slower` |
| 1358-1361 | `.history-verdict-behaviour-changed, -reordered, -unstable-shape, -alternating` (`-alternating` added by 3.18.0, 5b1a8e16) |
| 1362-1365 | `.history-verdict-quarantined` (striped gradient) |
| 1366-1372 | `.history-section` |
| 1373-1375 | `.history-section > summary` |
| 1376-1381 | `.history-summary-line` |
| 1382-1386 | `.history-meta, .history-note, .history-compare` (`.history-compare` added by 3.12.0, 6ee5adde) |
| 1387-1392 | `.history-charts` |
| 1393-1396 | `.history-chart` |
| 1397-1401 | `.history-chart-title` |
| 1402-1408 | `.history-chart-svg` |
| 1409-1420 | `.history-bar-pass`, `.history-bar-fail`, `.history-bar-duration`, `.history-bar-partial` |
| 1421-1437 | `.history-list h4`, `.history-count`, `.history-list ul`, `.history-list li` |
| 1438-1451 | `.history-evidence`, `.history-series`, `.history-more` |

Size: **4,226 B LF / 4,362 B CRLF** (4,227 / 4,364 with the blank line).

### 1c. Media-query copy (history-only)

- `@media (max-width: 480px) {` opens at line 1943 and closes at 2011. Inside it, lines 1995-1997 hold
  `.history-sparkline { width: 3em; }` and line 1998 is blank. Size: **75 B LF / 78 B CRLF** (76 / 80 with
  the blank line). It came with 3.11.0.
- There are no other copies. The `@media (max-width: 768px)` block (1755-) and the `768.02-1160px` block
  (1749-) contain nothing for history or diagnostics.

### 1d. Dark mode, print, other sheets

- **There are none.** No `prefers-color-scheme` or `@media print` exists anywhere under `src/`.
- `context-menu-styles.css`, `inline-svg-styles.css`, `collapsible-notes-styles.css` and
  `internal-flow-popup-styles.css` carry nothing for either feature.
- The violet theme overlay (`Constants/Stylesheets.cs:28-112`, `VioletThemeStyleSheet`) carries nothing
  for either feature either.

### 1e. Totals

| | LF (as committed and built on Linux) | CRLF (Windows checkout) |
|---|---|---|
| History-only CSS (1b + 1c) | **4,301 B** | 4,440 B |
| Diagnostics-only CSS (1a) | **1,784 B** | 1,838 B |
| Both | **6,085 B** (9.9% of the 61,425 B sheet) | 6,278 B |

### 1f. Classes emitted with no CSS rule, and shared rules to keep

Classes HistoryHtml emits that have no CSS rule:
- `.history-body` (HistoryHtml.cs:107)
- the base `.history-bar` (227)
- `.history-link` (175)
- `history-verdict-unknown` (it falls back to the base pill grey)
- `history-verdict-stable`, which is never rendered: `Pill` and `GroupPill` skip Stable.

Shared rules the sections rely on, which must stay:
- `body { overflow-wrap: anywhere }` (stylesheets.css:1-7). This is what makes `a.history-link` and
  `.history-meta code` wrap in ReportSectionWidthTests:41-42.
- `.h2` (83, 1957), used by the History `<summary class="h2">`.

---

## 2. The `$` verdict sigil in the search DSL

### 2a. Today's code paths (every verdict-touching line)

**`advanced-search.js`** (9,799 B; +904 B in 3.11.0; its only commits are 4f79ae10 and c9c3b068):
- 9-11 `isAdvancedSearch`: `/&&|\|\||!!|\$\w/`. Any `$word` takes the advanced path. **Not verdict code.**
- 99-111 tokeniser: `$name` becomes `{type:'status', value: name.toLowerCase()}`; a bare `$` is dropped.
  **Not verdict code.**
- 226-229 parser: a `status` token becomes a `status` AST node. **Not verdict code.**
- 249-250: JSDoc `@param {object} [verdicts]`.
- 253: `function advancedSearchEvaluate(ast, searchText, tags, status, verdicts)`.
- 263-271, the verdict branch:
  ```js
  case 'status':
      // comment 264-267
      if (status.toLowerCase() === ast.value) return true;
      if (ast.value === 'passed' || ast.value === 'failed' || ast.value === 'skipped' ||
          ast.value === 'bypassed' || ast.value === 'skippedafterfailure') return false;
      return !!(verdicts && verdicts.has(ast.value));
  ```
- 273-279: `verdicts` threaded through `and`, `or` and `not`.
- 289, 294: `advancedSearchMatch(input, searchText, tags, status, verdicts)` passes it through.

**`report-scenario-feature-map-helper.js`** (`fc()`; +221 B):
- 14-16: reads `data-history-verdicts` and adds `verdicts: hv ? new Set(hv.split(',')) : null` to each
  cached item.
- `fc()` walks `features[fi].getElementsByClassName('scenario')`. That covers `details.scenario` and
  `details.scenario.scenario-parameterized`, but never `<tr>` rows, whose classes are
  `row-passed|row-failed|...` (ReportGenerator.cs:2891-2898).

**`report-search-function.js`** (+15 B):
- 62: `advancedSearchMatch(input, item.searchText, tags, item.status, item.verdicts)`.
- This is the only verdict-related line. The legacy path (114-196) and row highlighting (82-107) use only
  text and phrase tokens.

**`report-search-index.js`** (+155 B):
- 251, 253: `kronDeepMatchesItem(input, deepInput, corpus, tags, status, verdicts)` passes it through.
- 358, worker verify: `item.verdicts ? new Set(item.verdicts) : null`.
- 481, `collectItemMeta` payload: `verdicts: c.items[i].verdicts ? Array.from(c.items[i].verdicts) : null`.

Total verdict-only JS is about **1,295 B** (LF), and it ships in every HTML report.

### 2b. What removing verdict support changes, function by function

**`advancedSearchEvaluate`.** `case 'status'` returns to its exact pre-3.11.0 form:
`return status.toLowerCase() === ast.value;`. The fifth parameter and its threading go.
- `$failed`, `$passed`, `$skipped`, `$bypassed` and `$skippedafterfailure` are unchanged.
- `$flaky`, `$broke`, `$stable`, `$new`, `$always-failing` and every other `$foo` evaluate to `false` for
  every scenario.

**`advancedSearchMatch`.** The signature returns to 4 arguments. The result for `$foo` is `false`, never
`null`, so `run_search_scenarios` never takes the legacy fallback.

**`isAdvancedSearch`, tokeniser, parser.** **No change.** `$foo` stays a `status` token. It is **neither
plain text nor an error.**

**`fc()`.** Drop lines 14-16 (the `hv` read and the `verdicts:` field). Nothing else reads the field.

**`run_search_scenarios`.** Line 62 drops the fifth argument. If a stray fifth argument were left in, JS
would ignore it.

**`kronDeepMatchesItem`, worker loop, `collectItemMeta`.** Lines 251/253, 358 and 481 lose the argument
and the field. `buildWorker` (491-503) serialises the functions with `toString()`, so the worker picks up
the reverted matcher automatically. There is no second copy to keep in sync.

**`kronIsDeepEligible`, `kronCandidateDocsForQuery`.** **No change.** `$...` has no text term, so it is not
deep-eligible. A status term never prunes (`default: // tag, status, not — never prune`, line 188).

**Resulting query semantics** (the same as a report without a ledger today):
- `$flaky` hides every scenario. The worker is not engaged and the chip stays hidden.
- `$failed && $broke` matches nothing.
- `$failed && !!$flaky` is the same as `$failed`.
- `$flaky || checkout` is the same as `checkout`.
- `!!$flaky` matches everything.

**There is no empty-state message.** No "no matches" or `no-results` element exists in the page, so the
list simply goes empty.

**What does not help.** Making the parser reject unknown `$` names does not give the user an error either.
A parse error returns `null`, which falls back to legacy text search (`run_search_scenarios` 111-160), and
legacy search looks for the literal text `$flaky` in `data-search`. That is again "nothing matches".

**Stale links.** `update_url_hash` writes `q=` (report-url-hash-function.js:69). A bookmarked
`#q=%24flaky` (or `$broke`, `$stable`, …) from a history-enabled report will open an empty report after
the move.

**Verdict vocabulary that stops matching.** From `HistoryVerdictNames` in HistoryVerdicts.cs:65-81:
stable, new, broke, failing, always-failing, fixed, flaky, slower, behaviour-changed, reordered,
alternating, unstable-shape, quarantined, unknown.

### 2c. Kronikol4J byte parity

Kronikol4J's assets are under
`C:\Code\Kronikol4J\kronikol4j-report\src\main\resources\io\kronikol\report\assets\`.

| Asset | Kronikol4J blob | .NET pre-3.11.0 | .NET now |
|---|---|---|---|
| `advanced-search.js` | 908e62c2 | **908e62c2** | 6002357e |
| `report-scenario-feature-map-helper.js` | bb84c91c | **bb84c91c** | 6cf32215 |

**An exact revert of these two files restores byte parity with the Java port.**

The other assets do not regain parity:
- `report-search-function.js`: K4J's blob is a9dab35a, the pre-3.11.0 .NET blob was 44b4a055, so they
  differ regardless.
- `report-search-index.js`: K4J has no copy.
- `stylesheets.css`: K4J's copy (a21a23a5) equals .NET's at 7b5a23a2 (2026-06-21), so removing the
  history CSS does not restore parity.

Record the change in K4J `docs/REMAINING_PARITY.md`. Lines 1987-2010 describe the 3.11.0 additions and
lines 1905-1915 describe the 3.21.0 sections going off by default.

### 2d. Where the sigil is documented

**In the page: nowhere.**
- The placeholder at ReportGenerator.cs:1611 reads
  `"Search... (@tag, $status, &&, ||, !!, parentheses)"`.
- The help table (1614-1627) has a `$status` row (1624) with examples `$failed`, `$passed`, `$skipped`.
  Neither mentions verdicts, so the in-page help needs no change.

**Outside the page:**

| Where | What |
|---|---|
| README.md:16 | "as a sparkline and verdict in the report, … and `$flaky` in the search box" |
| nuget-readme.md:65 | "the report (a sparkline and verdict per scenario, `$flaky` in the search box)" |
| Wiki `Search-Syntax.md` | 123-142 "History verdicts" section; 165-166 examples table |
| Wiki `FAQ.md` | 106-108 |
| Wiki `Generated-Reports.md` | 498 (export keeps sparklines and pills; section left behind) |
| Wiki `Cross-Run-History.md` | 13 mentions |
| Wiki, other | `Report-Configuration.md` (7), `Diagnostics-and-Debugging.md` (44-48, 147), `Merging-Parallel-Reports.md`, `Ingesting-External-Captures.md`, `Home.md`, `Exporting-to-OpenTelemetry.md` (1 each) |
| Code comments | HistoryHtml.cs:9-19; advanced-search.js:264-267; report-scenario-feature-map-helper.js:14; ReportGenerator.cs:2762-2763 |
| Tool help | MergeCommand.cs:383 (`--history` "the History section, sparklines and verdict pills in the HTML"); IngestCommand.cs:689-692 (`--diagnostics-section`) |
| `tools/history-bench/` | `dsl.js`, `prune.js` and `README.md:90-123` exercise `$flaky` against the shipped JS. A bench, not a test |
| `plans/DOORSTEP_PLAN.md:166` (F18) | The landing page's first paragraph, which lives outside this repo, mentions `$flaky` and a sparkline |

---

## 3. The deep-search index (C# and JS): does it carry verdicts?

**No.**

**The C# index has no verdict code.** `src/Kronikol/Reports/SearchIndex/{SearchIndexBuilder,
SearchNormalizer, SearchIndexBuildCache}.cs` contain no `verdict` or `history`. The KSI1 layout
(SearchIndexBuilder.cs:107 `Serialize`) holds only doc anchors and trigram buckets.

**No index piece includes verdicts.** The corpus pieces are collected at the emission sites in
ReportGenerator.cs:
- 2026-2042: `data-search`, which is the feature and scenario names, description, endpoint, rule, labels,
  categories, error message, steps, diagram terms and example values. **Verdicts are not included.**
- 2109, 3158: stack traces.
- 2223, 3276-3297: diagram sources.
- 2271-2272, 3317-3365: whole-test-flow pieces.
- 2825-2827: group search text.

**What follows:**
- `$flaky` is never indexed and never deep-eligible.
- Verdicts reach the worker only through the `init` message: `collectItemMeta()` at
  report-search-index.js:452-489, field at 481. They are rebuilt into a Set on each verify (358).
- `kron-search-index` and `puml-data` are unaffected by the move.

---

## 4. URL hash and anchors, keyboard, init, toggles, collapse/expand-all, storage, export, timeline

### 4a. Anchor handling

- **No client code knows `#history-section`.** `current_url_anchor()` (report-url-hash-function.js:1-9)
  recognises only the `scenario-` and `sid-` prefixes. A `#history-section` URL would work only through the
  browser's native fragment scroll; `parse_url_hash` ignores it and `update_url_hash` drops it on the next
  filter. Nothing in src links to `#history-section` or to the diagnostics block.
- **The `history-link` handler** (inline `onclick` at HistoryHtml.cs:175):
  ```
  event.preventDefault();
  if (window.reveal_url_anchor) { reveal_url_anchor('sid-<id>'); }
  history.replaceState(null, '', location.pathname + location.search + '#sid-<id>');
  ```
  It is the **only history caller** of `reveal_url_anchor`. The other callers are shared infrastructure
  that must stay:
  - the `hashchange` listener (report-init-script.js:6-9);
  - `parse_url_hash` (report-url-hash-function.js:165);
  - the Failures.md and `kronikol query` `#sid-` links.

  `element_for_stable_id` (10-24) and `jump_into_view` (47-65) are shared as well. Here `history` is
  `window.history`, not cross-run history; so is `history.replaceState` in report-export-function.js:42
  and report-url-hash-function.js:93.
- **For the separate document:** use plain links to `<HtmlTestRunReportFileName>.html#sid-<stableId>`. On
  load, `report-init-script.js:55-57` calls `parse_url_hash()`, which calls `reveal_url_anchor`. That opens
  every enclosing `<details>`, selects the displayed copy of an outline row, and jumps.
  - It is covered by `StableIdDeepLinkTests` (E2E; 10 facts: on load, pasted into an open report, rows,
    far down the page, phone width).
  - `QueryCommand.Narrative.cs:758-762` already builds this link, and only when the HTML sits beside the
    data file.
  - The file name is configurable (`options.HtmlTestRunReportFileName`), and merge writes the `-o` name.
  - **TestRunReport.html needs no new client code for this.**

### 4b. Keyboard, init, toggles, collapse/expand-all, storage, timeline

- **Keyboard navigation** (report-keyboard-navigation-function.js:1-34) walks visible `details.scenario`
  only. It is not history-aware and is unaffected.
- **report-init-script.js** (1-58) handles hashchange `#sid-`, the back-to-top button, the mobile filter
  fold and mobile diagram settings. Nothing about history or diagnostics.
- **toggle-script.js** (1-44) handles the iflow and diagram-type toggles only.
  `flame-chart-render-script.js:190-194` listens for `toggle` on any `details`, which is harmless for the
  sections because they hold no flame charts.
- **Collapse/expand-all** (report-collapse-expand-all-function.js:1-6) receives the selectors
  `'details.feature'` and `'details.scenario'` (ReportGenerator.cs:1705). It never touches
  `#history-section` or `.report-diagnostics`.
- **Persistent filters and storage: none.** There is no `localStorage`, `sessionStorage` or `indexedDB`
  in any Reports/*.js. `report-persistent-filter-function.js` holds two no-op stubs. All filter state
  lives in the URL hash (`q`, `status`, `deps`, `depmode`, `catmode`, `hp`, `cats`, `dur`, `pctl`), and
  none of it is history-specific.
- **Timeline:** there is no script relation. The History section is emitted immediately before
  `#scenario-timeline` (ReportGenerator.cs:1839-1855). `toggle_timeline` and `toggle_component_diagram`
  (1245-1275) do not know about it. The only shared thing is that the export leaves both behind.
- **`clear_all_filters`** (report-export-function.js:1-43) contains nothing history-specific.

### 4c. Export (report-export-function.js)

**`export_html`** (110-132):
- It copies:
  - `head.innerHTML`, which is the **whole stylesheet** (history and diagnostics CSS included) plus
    **every head script** (verdict JS included);
  - `<h1>Filtered Report</h1>`;
  - a clone of each visible `.feature` (`export_undrawn`, 90-109);
  - the body-level `<script>`s (`export_data_scripts`, 52-79). `puml-data` is pruned and every other
    script, including `kron-search-index`, is copied whole.
- **What travels with the features:** every sparkline, verdict pill and `data-history-verdicts` attribute
  inside them. That covers scenario `<details>`, parameterized groups and outline `<tr>` rows.
- **What is left behind:** the History section and the Report diagnostics section, because both sit
  outside `.feature`.
- The export has no search bar or toolbar, so `$flaky` cannot be used in it. `parse_url_hash`'s `q=` is a
  no-op there because `#searchbar` is null.
- Pinned by `HistoryExportTests.The_filtered_export_keeps_the_sparklines_and_leaves_the_section_behind`
  and the wiki's `Generated-Reports.md:498`.
- After the move, the exported head stops carrying the dead CSS and JS automatically.

**`export_csv`** (133-151): the Scenario column is
`(d.el.querySelector('summary.h3') || …).textContent.trim()`.
- That summary contains, in order (ReportGenerator.cs:2081 for scenarios, 2861 for groups):
  1. the display name;
  2. the Happy Path label;
  3. the labels;
  4. the duration badge;
  5. **the history pill text** (`broke`, `flaky ×3`);
  6. the 📋 and 🔗 button glyphs.
- Removing the pills changes the CSV's Scenario values. See 8c: this is already a bug.

---

## 5. DiagnosticsOpen

- **Server-side only. No client script reads it**, and it is not reflected into any data attribute.
- Path:
  1. `ReportToggleDefaults.DiagnosticsOpen` (ReportToggleDefaults.cs:86-89);
  2. `ResolvedToggleDefaults.DiagnosticsOpen` (ReportToggleDefaultsResolver.cs:37, 88);
  3. `ReportGenerator.cs:1828`, `RenderReportDiagnostics(diagnostics, toggles.DiagnosticsOpen)`;
  4. the bare `open` attribute at 4130.
- The only `data-toggle` attribute in the page belongs to the diagram filter buttons
  (ReportGenerator.cs:966).
- No JS touches `.report-diagnostics`. The section is a pure `<details>` with no links. `ScenarioId` is
  printed as `[id]` text, and it is the test id, not the stable id, so it could not be a `#sid-` link as
  it stands.
- Tests: `ToggleDefaultsMarkupTests.cs:466-467` (`<details class="report-diagnostics" open>`) and
  `ReportToggleDefaultsResolverTests.cs:49`.

---

## 6. Other page chrome

**Nothing else mentions history or diagnostics:**
- The status filter buttons (ReportGenerator.cs:1632-1641) are execution results.
- The toolbar row (1699-1730) has expand/collapse, Scenario Timeline, Component Diagram and the
  details/headers/assertions/steps/databases toggles. No history or diagnostics button.
- There is no legend element in the page and no verdict chip.
- The header and CI boxes carry no history counts.
- The run-end `history:` line (ReportGenerator.cs:639-643) is the console pointer, not HTML.

**Look-alikes that are NOT part of this move:**
- The internal-flow popup's `<details><summary>Diagnostic info` comes from
  InternalFlowHtmlGenerator.cs:344 and 372. It is inline-styled, rendered by
  internal-flow-popup-script.js:182-185, and is a separate feature.
- `DiagnosticReport.html` (DiagnosticReportGenerator.cs, under `DiagnosticMode`) is already a separate
  document with its own inline `<style>` (line 48). It is a precedent, but its directory resolution
  differs; see 8e.

---

## 7. Test-visible identifiers and who depends on them

### 7a. Globals and data attributes

| Identifier | Kind | Used by (client) | Used by (tests and tools) | Fate |
|---|---|---|---|---|
| `advancedSearchEvaluate(…, verdicts)` | global fn, 5th arg | `advancedSearchMatch`, worker (toString) | `JintTestBase.cs:82-96` (`CallEvaluate(..., verdicts)`), `VerdictSearchTests.cs:60-65`, `tools/history-bench/dsl.js:19` | Signature back to 4 args |
| `advancedSearchMatch(…, verdicts)` | global fn, 5th arg | `run_search_scenarios:62`, `kronDeepMatchesItem:253` | `JintTestBase.cs:69-76` (`CallMatch(..., verdicts)`, `SetVerdicts` 101-111), `VerdictSearchTests.cs:14-57` | Signature back to 4 args |
| `kronDeepMatchesItem(…, verdicts)` | global fn, 6th arg | worker loop 358 | `VerdictSearchTests.cs:95-104` (6-arg calls), `:107-118` (regex over the shipped file asserting `verdicts`, `item.verdicts`, `verdicts: c.items[i].verdicts`); `SearchIndexJintTests.cs:171` calls it with 5 args and **survives** | Back to 5 args |
| `fc()` item `.verdicts` | cache field | `report-search-function.js:62`, `report-search-index.js:481` | none directly | Removed |
| `data-history-verdicts` on `details.scenario` and `details.scenario-parameterized` | attribute | `fc()` (feature-map helper:15) | E2E `HistorySparklineTests` 29/49/107, `HistoryExportTests:31`; unit `HistoryOutputsTests` 297/304/323/372, `MergeCommandTests:221`, `HistoryHtmlTests` (several, 203) | Gone from the report |
| `data-history-verdicts` on `<tr>` rows | attribute | **no client reader** (inert today) | `HistoryHtmlTests.cs:208-234` only | Gone |
| `id="history-section"`, `.history-*` classes | DOM | none (CSS only) | E2E `HistorySectionTests` (4 facts), `HistorySparklineTests` (4), `HistoryExportTests` (1), `ReportSectionWidthTests` 41-42 and 53 (`closest('.failure-clusters, .history-section, .filtering-box')`), `ViewportSweepTests` 100-106 and 127-129 via `ReportTestHelper.GenerateReportWithEverySection` (2798-2887, `showHistorySection: true`, `showReportDiagnostics: true`); unit `HistoryHtmlTests` (8 facts), `HistoryOutputsTests` 296-327 and 389, `MergeCommandTests` 220-231 | Move to the new document's tests |
| `a.history-link` + `reveal_url_anchor` | DOM + global fn | inline handler | `HistorySectionTests.A_link_in_the_section_opens_the_scenario_it_names` (50-57) | Becomes a cross-document `#sid-` link |
| `.report-diagnostics*` | DOM | none | `IngestHostDiagnosticsTests.cs` 80-82, 123-125, 144-145, 156, 162-175 (`RenderReportDiagnostics`); `ToggleDefaultsMarkupTests.cs:466-467`; `IngestCommandTests.cs` 457 and 470; `ReportGeneratorDiagnosticsScopeTests.cs:61` (see 8a); `ViewportSweepTests` via `GenerateReportWithEverySection` | Move to the new document's tests |
| `reveal_url_anchor`, `current_url_anchor`, `element_for_stable_id`, `jump_into_view` | global fns | shared | `DeepLinkReportTests` 69-76, `ParameterizedGroupRenderTests:513`, `StableIdDeepLinkTests:190`, `ClearAllFiltersReportTests:72`, `FailureClusterReportTests:253` | **Keep** (shared) |

### 7b. E2E history facts that test removed behaviour

These are in tests/Kronikol.Tests.EndToEnd:

- `HistoryFilterTests` (3 facts): `$flaky`; `$failed && $broke`; `$passed && $flaky`; `$flaky || $broke`;
  `$stable`; `$failed && !!$flaky`; `$failed`/`$passed` still meaning the status.
- `HistorySparklineTests` (4 facts).
- `HistorySectionTests` (4 facts).
- `HistoryExportTests` (1 fact).
- `HistoryReportHelper.cs` (the seeded ledger).
- `test.runsettings:4-12` sets `KRONIKOL_HISTORY=off` process-wide. Tests of the feature name a ledger
  explicitly.

### 7c. Plan harnesses, tools and an unaffected pin

- Plan harnesses that reference both sections:
  - `plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.harness/panels.js:33`
  - `plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.harness/sections.cs:71, 96`
- Tools:
  - `tools/history-bench/jsd/export.js:10, 34` (a mock `history-section` for the export check)
  - `tools/history-bench/dsl.js`
  - `tools/history-bench/prune.js`
- Unaffected: `ToggleDefaultsBaselineTests` is a relative A/B/C byte-identity pin, not a golden file.

---

## 8. Pre-existing defects and traps found along the way

**a. `ReportGeneratorDiagnosticsScopeTests.A_normal_run_records_its_diagnostics_in_the_json`
(tests/Kronikol.Tests/Reports/ReportGeneratorDiagnosticsScopeTests.cs:61) passes only because of a CSS
comment.**
- The assertion is `Assert.Contains("Report diagnostics", File.ReadAllText(...TestRunReport.html))`.
- The options (30-40) leave `ShowReportDiagnosticsSection` false, so no section is rendered.
- The only matching text in the file is the comment `/* Report diagnostics (capture health, skipped lines,
  render failures) */` at stylesheets.css:1261.
- The JSON assertion on line 60 is the real check.
- Deleting the diagnostics CSS turns this test red for the wrong reason. Repoint it, or drop the bare
  substring check. This is the "never bare substrings" rule from 3.0.82.

**b. `MergeCommandTests.cs:222`, `Assert.Contains("history-sparkline", html)`, is always satisfied by the
stylesheet's `.history-sparkline {` rule (1317).**
- It cannot fail while the CSS exists.
- Lines 220-221 next to it are anchored properly (`<details id="history-section"` and
  `data-history-verdicts="broke"`).
- It should be `class="history-sparkline"`, as `HistoryOutputsTests:298` and `HistoryExportTests:30`
  already do.

**c. Export Filtered CSV puts badge text into the Scenario column**
(report-export-function.js:141; untested apart from `ExportFilteredViewReportTests.cs:54-58`, which only
checks that `export_csv` exists).
- It takes `summary.h3` textContent, so a row's Scenario reads like
  `Pay by card 120ms broke📋🔗`: the duration badge, labels, the history pill (`broke ×2` on groups) and
  the copy and link glyphs.
- The wiki (`Generated-Reports.md:498`) promises "feature, scenario, status, duration".
- Fixing it (`data-scenario-name` on the copy button, or the summary's first text node) is independent of
  the move. The move alone would remove only the pill part.

**d. The `$` sigil fails silently.** After the move, any `$verdict` query or bookmarked `#q=%24flaky`
empties the list with no message. This already happens in every report without a ledger. Decide in the
plan whether a hint is wanted; no channel for one exists today.

**e. Directory resolution: use the run's directory, not DiagnosticReportGenerator's pattern.**
- `DiagnosticReportGenerator.Generate` (DiagnosticReportGenerator.cs:30-33) writes to
  `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, options.ReportsFolderPath)`.
- That ignores the ambient `ScopeReportsDirectory` that `kronikol merge` sets (ReportGenerator.cs:128-133).
- For a blank `ReportsFolderPath` it differs from `ResolveReportsDirectory`, which falls back to `Reports`
  (107-113).
- A new history or diagnostics document should resolve its directory as TestRunReport.html does.
- It must also be added to `PlannedFiles` (ReportGenerator.cs:736-772), so the manifest and run rotation
  (`runs/<id>/`) move it with the report. That keeps its relative `TestRunReport.html#sid-` links valid.
  (This is server-side; it is noted because it decides whether the links work.)

---

## 9. What the move would delete client-side (checklist)

1. stylesheets.css 1261-1315 (diagnostics), 1316-1452 (history) and 1995-1998 (mobile copy). About 6.1 KB.
2. advanced-search.js: restore the exact pre-3.11.0 bytes (`git show c9c3b068^:src/Kronikol/Reports/advanced-search.js`).
   That gives K4J parity.
3. report-scenario-feature-map-helper.js: restore the pre-3.11.0 bytes. That gives K4J parity.
4. report-search-function.js:62: drop `, item.verdicts`. Its pre-3.11.0 form differed only by this.
5. report-search-index.js:251, 253, 358, 481: drop the verdict argument and field. A plain revert of
   c9c3b068's hunks is not possible, because 512bc85a (3.30.1) changed the file since.
6. Nothing in: report-url-hash-function.js, report-init-script.js, report-export-function.js (no history
   code; its behaviour follows the DOM), keyboard, collapse, toggle and timeline scripts, the PlantUML
   scripts (plantuml-browser-render-script.js, plantuml-worker-host.js, PlantUml/plantuml-render.js), the
   context menu, collapsible notes (whose own "verdict" at 553 and 1549 is a note-cache term), or
   agent-instructions.md (it mentions only `kronikol query history`).
7. Tests to delete, move or rewrite: see 7a and 7b. Fix 8a and 8b in the same pass, since both turn red
   (8a) or meaningless (8b) when the CSS goes.
8. Docs: README.md:16, nuget-readme.md:65, the wiki pages in 2d, MergeCommand.cs:383 help text,
   IngestCommand.cs:689-692 help text, and the K4J ledger.
