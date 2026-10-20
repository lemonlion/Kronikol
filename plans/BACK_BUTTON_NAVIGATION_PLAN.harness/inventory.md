# Back as navigation: code inventory

Read-only inventory for the Back-button plan. **v5 = local branch `v5` at `d385c715`** (Option C), **4.x = `origin/main` at
`17914701`** (4.32.0). Every `file:line` is on v5 unless marked 4.x. Each line was re-read before it was written down.
Every "Back right after" answer is **reasoned from code** (and from the HTML spec where it says so); nothing was run.

Written in a session scratchpad from read-only copies of the sources (`v5all/`, `mainall/`, `v5tests/`, `optionc.md`,
`sab.md`), which are not kept here. Read the same files with `git show d385c715:<path>` (the `v5` branch then) and
`git show 17914701:<path>` (4.32.0); `optionc.md` is `plans/OPTION_C_PLAN.md` and `sab.md` is
`plans/OPTION_C_PLAN.harness/inventory/structure-and-behaviour.md`, both on `v5`.

## File legend

| Short | Path (all under `src/Kronikol/` unless shown) |
|---|---|
| `hash` | Reports/report-url-hash-function.js |
| `init` | Reports/report-init-script.js |
| `tabs` | Reports/report-tabs-script.js |
| `ftools` | Reports/report-features-tools-script.js |
| `fbar` | Reports/report-filter-bar-script.js |
| `chips` | Reports/report-filter-chips-script.js |
| `search` | Reports/report-search-function.js |
| `export` | Reports/report-export-function.js (holds `clear_all_filters` too) |
| `jump` | Reports/report-jump-to-failure-function.js |
| `keys` | Reports/report-keyboard-navigation-function.js |
| `lightbox` | Reports/report-lightbox-function.js |
| `popup` | Reports/internal-flow-popup-script.js (line 1 is `<script>`) |
| `toggle` | Reports/toggle-script.js (line 1 is `<script>`) |
| `settings` | Reports/report-scenario-settings-script.js |
| `selrow` | Reports/report-select-row-function.js |
| `ctx` | Reports/context-menu-script.js |
| `render` | Reports/plantuml-browser-render-script.js |
| `theme` | Reports/report-theme-switch-script.js |
| `tl` / `comp` | Reports/report-timeline-script.js / Reports/report-components-script.js |
| `map` | Reports/report-scenario-feature-map-helper.js (`fc`, `applyVisibility`, readout) |
| `RG` | Reports/ReportGenerator.cs |
| `HH` | History/HistoryHtml.cs |
| `LRG` | Reports/LabsReportGenerator.cs |

## 0. The mechanism in five lines

1. **Nothing pushes.** No `pushState`, no `location.hash =` in product code, no `popstate` listener, no
   `history.scrollRestoration`, no `pageshow`/`pagehide`, no `sessionStorage` (full grep of `v5all/`).
2. **Four `replaceState` call sites:** `update_url_hash` (`hash`:101, reached from every filter), `clear_all_filters`
   (`export`:35), the failure-cluster link's inline onclick (`RG`:2111) and the History tab link's inline onclick
   (`HH`:143).
3. **One element that makes a history entry:** the per-scenario "Link to this scenario" anchor
   `a.scenario-link href="#scenario-<slug>"` (`RG`:2417, :3489), a native fragment navigation. Plus anything typed or
   pasted into the address bar, and cross-document links (labs page, non-image attachments).
4. **Three `hashchange` listeners** (`ftools`:178, `tabs`:41, `init`:6-9), each re-testing "first segment starts
   `scenario-`/`sid-`"; none re-applies filters.
5. **The hash holds only filters and one anchor.** Tab, Features view, sort, open sections, selected example row (unless
   addressed by `#sid-`), diagram view, layers, More filters, timeline/component controls and scroll are not in it.

The Specifications report runs the same scripts with no tabs (ReportTabsTests.cs:158), so everything below except
tabs applies to it too.

**Note B (used below).** "Back skips it" means: the interaction made no history entry, so Back goes to the entry before
the current one. Usually that is **the page the report was opened from** (labs page, CI artifact page, file listing,
nothing in a fresh tab): the report is left and its tab, view, open sections, layers and scroll are lost; Forward (or
reopening) reloads it from its last URL, so `parse_url_hash` restores the filters and re-reveals the anchor, and
everything else starts as the generator wrote it (unless the browser restores the live page from its back/forward
cache; not verified for `file://`). If an earlier same-document entry exists (the reader clicked a 🔗 or pasted a
fragment earlier), Back goes there instead: `hashchange` fires, and only if that entry's first segment names a scenario
does the page switch to Features/Full and re-reveal it; filters are not re-applied.

## 1. Interaction inventory (v5)

URL column: `none`, `replaceState`, `native #` (fragment navigation), `document` (cross-document navigation),
`new tab`. Entry = a new session history entry in the report's tab.

### 1A. Tabs and Features views

| # | Interaction (trigger) | Handler | What changes | URL | Entry | Back right after (reasoned from code) |
|---|---|---|---|---|---|---|
| A1 | Report tab click: `button.report-tab[data-tab]` in `.report-tabs` (`RG`:1974-1980, markup `RG`:2861-2863) | `tabs`:45-46 → `activate` `tabs`:10-25; also `fbar`:243 re-fits the bar | panels' `hidden`, `aria-selected`, `tabindex`, `.on-features` on the bar, draws the panel's diagrams, fires `resize` | none (`activate` never touches the URL) | no | Back skips it (Note B). The previous tab is not restorable. Window scroll is shared by all panels and is neither saved nor restored per tab (`tabs`:10-25 has no scroll code) |
| A2 | Arrow keys / Home / End on a tab | `tabs`:53-66 → `activate` + focus | same as A1 | none | no | skips (B) |
| A3 | Full / Summary: `button.view-btn[data-view]` in `.view-switch` (`RG`:3046-3053) | `ftools`:138-139 → `setView` `ftools`:10-29 | `hidden` on `#features-table-view` / `#report-content`, `.features-table-mode` on the bar, draws cards' diagrams, `resize` | none | no | skips (B); the view is not restorable |
| A4 | Arrow keys / Home / End on a view button | `ftools`:152-165 | as A3, focus moves | none | no | skips (B) |
| A5 | Summary row opens its card: any click in `#features-table-view tbody tr`, incl. `button.feature-open` (`RG`:1751-1753) | `ftools`:147-148 → `openRow` `ftools`:80-92 | `setView('full')`, `card.open = true`, `card.scrollIntoView({block:'start'})`, focus on the card's summary | none | no | skips (B): the table and its scroll position are not reachable by Back |
| A6 | Sort select `#features-sort` (`RG`:3079-3087, select `RG`:3085) | `ftools`:169-171 → `sortBy` `ftools`:55-77 | cards and table rows moved in the DOM; column sort marks cleared | none; not in the hash | no | skips (B). See §6 S10 (no `autocomplete="off"`) |
| A7 | Column sort: `th onclick="sort_table(n)"` (`RG`:1722-1739) | Reports/report-sort-table-function.js:1 | table rows only | none | no | skips (B) |
| A8 | Expand / collapse all features or scenarios: `button.fold-btn[data-fold]` (`RG`:3060-3072) | `ftools`:133-137 → `fold` `ftools`:118-128, dimming `ftools`:96-115, `toggle` capture `ftools`:172 | `open` on every `details.feature` / `details.scenario` (and their rules/features when expanding scenarios) | none | no | skips (B) |
| A9 | Phone "Display settings" gear `button.tools-gear` (`RG`:3090-3091, written only with tabs and BrowserJs, `RG`:1995) | `ftools`:140-146 | `.tools-open` on the bar, `aria-expanded` | none | no | skips (B); the panel stays open while the page is left |

### 1B. Disclosure, scenario focus, jumps

| # | Interaction | Handler | What changes | URL | Entry | Back right after |
|---|---|---|---|---|---|---|
| B1 | Open/close a feature, rule, scenario or parameterized group by its `<summary>` (`RG`:2238, :2296, :2416, :3488) | native `<details>`; in History/Diagnostics tabs the summary click is cancelled (`tabs`:51) | `open` attribute | none | no | skips (B); open state is lost on reload except via the anchor |
| B2 | Arrow Down / Up (focus not in a field) | `keys`:14-29; capture first, in this order: `ftools`:167 → `setView('full')`, `tabs`:68 → `toFeatures` | `.scenario-focused` moves, `scrollIntoView({behavior:'smooth',block:'center'})`, its feature opened | none | no | skips (B). See §6 S11 for a focused tab |
| B3 | Enter with nothing focused | `keys`:30-36 | toggles `open` on the focused scenario | none | no | skips (B) |
| B4 | `/` | `keys`:4-9 | focuses `#searchbar` | none | no | n/a |
| B5 | Next Failure `button.jump-to-failure` (`RG`:2599-2602) | capture `ftools`:150 (`setView('full')`), `tabs`:49 (`toFeatures`); then `jump_to_next_failure` `jump`:2-16 | opens feature + failed scenario, `jump_into_view(summary,'start')` (`jump`:13), counter `(i/n)` | none | no | skips (B): the place (and tab) it was pressed from is lost; the opened failure stays open |
| B6 | Back to top `button#back-to-top` (`RG`:2605), shown after 2 viewports (`init`:11-17) | inline `window.scrollTo({top:0,behavior:'smooth'})` | scroll | none | no | skips (B); does not return to the pre-click position |

### 1C. Inside a scenario

| # | Interaction | Handler | What changes | URL | Entry | Back right after |
|---|---|---|---|---|---|---|
| C1 | Diagram view tabs Sequence / Activity / Flame: `button.diagram-toggle-btn[data-dtype]` in `.diagram-toggle-tabs` (`RG`:1174-1175, :1198-1205); script only with internal-flow tracking (`RG`:1597) | `toggle`:22-48; arrows/Home/End `toggle`:50-63 | active tab, `.diagram-view-*` display, `.diagrams-not-seq`, draws | none | no | skips (B) |
| C2 | Whole-test-flow Activity / Flame `button.iflow-toggle-btn` | `toggle`:2-21 | view display, flame render | none | no | skips (B) |
| C3 | Scenario settings gear `button.scenario-settings-gear` (`RG`:1168-1169, placed `RG`:1206-1208) | `settings`:34-47 → `set` `settings`:21-32 | `.is-open` on `.scenario-settings`, `aria-expanded` on both gears | none | no | skips (B); bar stays open |
| C4 | Phone title gear `button.gear-in-summary` in the diagrams' `<summary>` (`RG`:1172, :1195) | `settings`:38-42 (same state as C3) | same | none | no | skips (B) |
| C5 | Example row select: `tr onclick="selectRow(this,'<prefix>')"` (flat `RG`:3575, grouped `RG`:3682); also fired by search (`search`:197-199, :283-285), by `.param-expand` toggle (Reports/report-param-expand-js.js:2-10), by `reveal_url_anchor` (`hash`:41-42) and by a cluster link (`RG`:2111 `el.click()`) | `selrow`:1-58 | `.row-active`, detail panel, sequence/activity/flame div for that row | none, although `#sid-<rowId>` addresses exist | no | skips (B); the row choice is not restorable |
| C6 | Flat / grouped parameter table `button.flatten-toggle` (`RG`:3505) | Reports/report-toggle-flatten-params-js.js:1 | which table shows | none | no | skips (B) |
| C7 | Step parameter reference `button.step-table-ref` (`RG`:4234) | Reports/report-toggle-table-ref-function.js:1-34 (smooth `scrollIntoView` :25) | scroll to the table, highlight | none | no | skips (B); not back to the step |
| C8 | Payload / diagram controls (Expanded/Collapsed/Truncated, line count, format, width, Headers/Assertions/Steps/Databases/Events/Setup; `RG`:1078-1110, :1225, :1554-1557) | Reports/collapsible-notes-script.js | redraws notes | none; not in the hash | no | skips (B) |
| C9 | Flame chart bar click (zoom) / double-click (reset) | Reports/flame-chart-render-script.js:114-136 | redraws the chart | none | no | skips (B) |
| C10 | Diagram select / zoom (click, Ctrl +/-, Esc deselects) | `ctx`:914-929, :1109+ | `.diagram-selected`, zoom | none | no | skips (B) |

### 1D. Filters

| # | Interaction | Handler | What changes | URL | Entry | Back right after |
|---|---|---|---|---|---|---|
| D1 | Search typing `input#searchbar onkeyup="search_scenarios()"` (`RG`:1830) | 300 ms debounce `search`:26-33 → `run_search_scenarios` `search`:115-292 → `update_url_hash` (`search`:223, :259; advanced path :137, :172) | flags + `applyVisibility` (`map`:36-61), a single match is opened (`search`:262-264), matching example rows clicked (`search`:283-285); **emptying the box closes every scenario** (`search`:217-221) | replaceState (`hash`:101), anchor kept first (`hash`:98-99) | no | skips (B); the typed text is not undone (every keystroke replaced the same entry) |
| D2 | Status `button.status-toggle onclick="toggle_status(this)"` (`RG`:1865) | Reports/report-status-filter-function.js:1-4 → `filter_statuses` :6-27 (`update_url_hash` :16, :26) | status flags | replaceState | no | skips (B) |
| D3 | Tag / dependency / category chips (click = off→has→has not; Alt+click and right-click = has not), Happy paths segments, ANY/ALL, a row's Clear | `chips`:119-146, :148-153 → `filter_tags` `chips`:73-80, Reports/report-dependency-filter-function.js:12-18, Reports/report-category-filter-function.js:12-18 (each ends in `update_url_hash`) | chip states, flags | replaceState | no | skips (B) |
| D4 | Duration percentile `button.percentile-btn onclick="set_percentile(this)"`, custom `input#duration-threshold onchange="filter_duration()"` (`RG`:1892) | Reports/report-duration-filter-function.js:1-47 (`update_url_hash` :9, :22) | duration flags | replaceState | no | skips (B) |
| D5 | Clear All: box `button.export-btn onclick="clear_all_filters()"` (`RG`:1829), bar `button.nav-clear` (`fbar`:153), empty state `button.filters-empty-clear` (`fbar`:154-159, also focuses the search) | `clear_all_filters` `export`:1-36 | every filter off, modes to the seeded defaults | replaceState to `#<anchor>` or nothing (`export`:34-35) | no | skips (B); Clear All cannot be undone |
| D6 | Empty state `div.filters-empty` shows when 0 scenarios remain | `fbar`:149 (passive) | | none | no | n/a |
| D7 | Deep search results arriving | Reports/report-search-index.js:594, :608, :621-638 | visibility only | none (q= already written) | no | n/a |

### 1E. Filter box, bar, small layers

| # | Interaction | Handler | What changes | URL | Entry | Back right after |
|---|---|---|---|---|---|---|
| E1 | More filters `button.more-filters` (`RG`:3027-3028) | `fbar`:291-297 | `.rows-folded` toggled (starts folded, `RG`:1828), count badge `fbar`:68-79 | none; not in the hash | no | skips (B) |
| E2 | Phone Filters toggle `div.mobile-filter-toggle` (`RG`:1831) | `init`:19-31 | `.filters` display | none | no | skips (B) |
| E3 | Filters (box into the pinned bar) `button.nav-filters-toggle` (`RG`:3034-3035) | `fbar`:238 → `open` `fbar`:192-215 / `shut` `fbar`:216-232 | the `.filtering-box` node moves into the bar and back; focus to the search | none | no | skips (B); box stays in the bar |
| E4 | Box out of the bar: close `button.filters-close` (`RG`:3038-3039) `fbar`:239; Esc (focus in box or group) `fbar`:240-242; bar comes to rest `fbar`:256; phone width `fbar`:264 | `shut` | as E3 | none | no | n/a |
| E5 | Search help `button.search-help-toggle onclick="toggle_search_help()"` (`RG`:1830) | `search`:3-9 | `.search-help-panel` display, `.help-open` | none | no | skips (B) |
| E6 | Theme switch `button.theme-toggle` (`RG`:95-96, placed `RG`:1692) | `theme`:31-33 (stores `kronikol.theme`, `theme`:21) | `data-theme` | none (localStorage only) | no | skips (B); persistent by design |
| E7 | Timeline controls Show / Warm-up / Order `button.tl-seg-btn` | `tl`:101-112 | rows shown, widths, order | none; not in the hash | no | skips (B) |
| E8 | Components controls Show / Direction / Call summaries | `comp`:184-195 | redraws the diagram | none; not in the hash | no | skips (B) |

### 1F. Every link to a scenario

| # | Link | Handler | What changes | URL | Entry | Back right after |
|---|---|---|---|---|---|---|
| F1 | **"Link to this scenario" 🔗** `a.scenario-link href="#scenario-<slug>" onclick="event.stopPropagation()"` on every scenario and group summary (`RG`:2417, :3489) | capture: `ftools`:150, `tabs`:49; then native fragment navigation and the browser's own scroll to the element; then `hashchange` → `ftools`:178, `tabs`:41, `init`:6-9 → `reveal_url_anchor` (`hash`:25-46: opens every enclosing `<details>`, centres it) | scenario revealed | **native #**: the whole fragment becomes `#scenario-<slug>`, so the filter parameters vanish from the URL while the filters stay applied | **yes** (per the spec a link to the URL already shown replaces instead) | Back returns to the previous entry: the browser restores that entry's scroll, then `hashchange` fires; if that entry's first segment names a scenario, `init`:8 re-reveals it and overrides the restored scroll (HTML spec order: popstate, restore persisted state, then queue hashchange); filters are not re-applied (unchanged anyway). The scenario stays open. **The one in-page action Back roughly undoes** |
| F2 | Failure cluster entry `a.failure-cluster-scenario-link href="#<anchor>"` in `details.failure-clusters` at the top of the Features panel (`RG`:2099-2116, link `RG`:2111) | capture `ftools`:150, `tabs`:49; inline onclick: `preventDefault`, opens every enclosing `<details>` and the scenario, `el.click()` for a row, `jump_into_view(el,'start')`, `replaceState(...'#<anchor>')` | scenario revealed | replaceState, fragment = the anchor only (filters dropped from the URL) | no | skips (B): Back leaves the report; the cluster list and its scroll are lost |
| F3 | **History tab entry** `a.history-link href="#sid-<id>"` (`HH`:139-144, emitted per entry `HH`:218 by `List` `HH`:210-224; section `HH`:113-117 into `#panel-history`, `RG`:2135, :2594) | capture `ftools`:150 (`setView('full')`), `tabs`:49 (`toFeatures`); inline: `preventDefault`, `reveal_url_anchor('sid-…')`, `replaceState(...'#sid-…')` | Features tab, Full view, scenario revealed | replaceState, fragment = `sid-…` only (filters dropped); no `hashchange` | no | **skips (B): Back leaves the report or lands on an older entry, never the History tab or its scroll. This is the owner's bug.** The tab is in no URL (`tabs`:10-25) |
| F4 | Diagnostics tab entries | none: the scenario id is text, `<span class="report-diagnostic-scenario">[id]</span>` (`RG`:4806-4807). Background calls name the scenario as text (`RG`:4859) | | | | no link today (`tabs`:47-48 says "a diagnostic's", stale) |
| F5 | Timeline bars and labels | none: `div.timeline-row[data-sid]` with a label `div` (`RG`:2178-2191); `tl`:46-58 reads `data-sid` for filtering only | | | | no link today |
| F6 | Component diagram edges | `[[#iflow-rel-<caller>-<service> …]]` (src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs:311, :321) open the internal-flow popup (G3) | | | | no scenario link |
| F7 | Sequence-diagram call links `#iflow-<segment>` | bound text click `render`:1442-1446, or `<a>` caught by `popup`:239-255 (`preventDefault`) | popup (G3) | none | no | |
| F8 | Flame chart | zoom only (`C9`) | | | | no link |
| F9 | Links inside the internal-flow popup | none: `fill` writes a diagram, a flame chart and view toggles (`popup`:114-207) | | | | no link |
| F10 | Labs page `TestRunReport.labs.html` (separate document, no script): each scenario `a.history-link href="<report>.html#sid-<id>"` (`HH`:134-137, via `LRG`:64 and `LRG`:108); "Open the report" `a.labs-back href="<report>.html"` (`LRG`:83-84) | browser | loads the report; `parse_url_hash` reveals | document | yes | Back returns to the labs page natively (the report's own state is lost, B) |
| F11 | Fragment typed or pasted into an open report's address bar | `hashchange`: H1-H3 (§2) | Features, Full, anchor revealed; **filter parameters in it are ignored** (`init`:2-9) | native # | yes | as F1 |

### 1G. Overlays, new tabs, copies, downloads

| # | Interaction | Handler | What changes | URL | Entry | Back right after |
|---|---|---|---|---|---|---|
| G1 | Image attachment `a.attachment-image-link onclick="openLightbox(event, this)"` (`RG`:2465, :3767, :4314) | `lightbox`:7-12 (`preventDefault`); close on overlay click `lightbox`:6, Esc `lightbox`:13 | `.lightbox-overlay.active` | none | no | skips (B) with the overlay still open; on a phone, system Back leaves the page |
| G2 | Non-image attachment `a.step-attachment href="attachments/…"` (`RG`:2469, :3771, :4319; href built `RG`:6673-6679), no `target` | browser | same-tab navigation to the file | document | yes | Back returns to the report document: from bfcache with state intact, or reloaded from its last URL (filters + anchor only) |
| G3 | Internal-flow popup: open from F6/F7 | `showPopup` `popup`:51-93 (replaces an open one, :52-53); close `×` `popup`:64, overlay click `popup`:74-76, Esc `popup`:257-262 | `.iflow-overlay` appended to `<body>` | none | no | skips (B) with the popup open |
| G4 | Diagram context menu (right-click) and its "Open … in new tab" items | menu `ctx`:492-886; close on click `ctx`:888-890, Esc :891-893, scroll :894; items `ctx`:790-831 → `openTab` `ctx`:252-254 (`window.open('', '_blank')`) + `writeTab` :256-261 (fallback blob URL :267, :277, :287); Save as PNG/SVG download :758-786 | new tab with the picture or text | new tab | no (in the report tab) | unaffected |
| G5 | Copy scenario name 📋 `button.copy-scenario-name` (`RG`:2417, :3489) | Reports/report-copy-scenario-name-function.js:1-10 | clipboard gets the **display name only** (`data-scenario-name`), not a URL. There is no copy-link button: the 🔗 (F1) navigates | none | no | n/a |
| G6 | Export Filtered HTML / CSV (`RG`:1829) | `export_html` `export`:103-125, `export_csv` `export`:135-157 (blob + `a.download` + `a.click()`) | download | none | no | n/a. The exported page copies the whole `<head>` (all hash scripts) but its body has no tabs or panels |
| G7 | CI "View run" `a.run-link target="_blank"` (`RG`:3148) | browser | new tab | new tab | no | unaffected |

## 2. hashchange / popstate listeners and the load path

### 2.1 Listeners

| # | Where | Registered | Phase | Order on a `hashchange` | Does |
|---|---|---|---|---|---|
| H1 | `ftools`:178 (predicate `scenarioInHash` `ftools`:174-177) | as the head's big inline `<script>` runs (its `featuresToolsScript` slot, `RG`:1653) | window, bubble | 1st | `setView('full')` if the first segment starts `scenario-` or `sid-` |
| H2 | `tabs`:41 (predicate `hashNamesScenario` `tabs`:35-38) | as `<script id="kronikol-report-tabs">` runs, in the head (`RG`:1683) | window, bubble | 2nd | `toFeatures()` (`tabs`:32) if it names a scenario |
| H3 | `init`:6-9 (predicate `current_url_anchor` `hash`:1-9) | inside `DOMContentLoaded` (`init`:1), so after H1 and H2 | window, bubble | 3rd | `reveal_url_anchor(anchor)`; filters deliberately not re-applied (comment `init`:2-5) |
| - | popstate | none | | | |

Related capture-phase `click` listeners on `document`, in registration order: `ftools`:130-151 (head big script), `popup`:239-255
(head, `RG`:1678 slot), `tabs`:42-52 (head, `RG`:1683); later `fbar`:78 and `fbar`:278 (registered in `start`, after
`DOMContentLoaded` + `setTimeout`). An element's inline `onclick` runs after all of them, so for F2/F3 the tab and view
switch first, then the inline handler reveals and calls `replaceState`. Capture `keydown`: `ftools`:152-168, `tabs`:53-69;
`keys`:1-37 is bubble.

The "names a scenario" test is written three times (`hash`:7-8, `tabs`:36-37, `ftools`:175-176).

### 2.2 Load path (v5)

1. Head, in order (`RG`:1625-1687): theme boot (`RG`:1633, `data-theme` from `localStorage`), then the big inline script
   (`RG`:1643-1672: decompress, advanced search, `map`, `chips`, `search`, deep search, dependency/category/status
   filters, `ftools` [registers H1 and its `DOMContentLoaded` start], `fbar` [`DOMContentLoaded` → `setTimeout(start,0)`],
   `lightbox`, ..., `jump`, duration, `export`, persistent-filter stubs, `hash`, `keys`, `init` [`DOMContentLoaded`]), then
   render, notes, context menu, flame, internal-flow data and popup, `toggle`, diagram layout (`RG`:1673-1680), theme
   switch (`RG`:1681), run band (`RG`:1682), `tabs` (`RG`:1683, registers H2 and its `DOMContentLoaded` start), scenario
   settings, timeline, components (`RG`:1684).
2. Body as written by the generator: **start tab** `RG`:1962 = `timeline` if `ScenarioTimelineVisible`, else `components`
   if `ComponentDiagramVisible`, else `features` (both default false, Reports/ReportToggleDefaultsResolver.cs:31-32, so
   Features); panels `hidden` except the start one (`RG`:2866-2867); **start view** `RG`:1965 Summary only if
   `FeaturesSummaryOpen` (default false, ReportToggleDefaultsResolver.cs:35); `open` on features/scenarios from the toggles.
3. `DOMContentLoaded` handlers in registration order: (a) `ftools` start `ftools`:180-184 (pair rows with cards,
   `setView('full')` if the hash names a scenario, fold dimming); (b) `fbar` schedules its start; (c) `lightbox`
   appends its overlay (`lightbox`:14); (d) **`init`** (`init`:1-40): registers H3, back-to-top, the phone filter fold,
   then `parse_url_hash()` if there is a hash (`init`:37-39); (e) flame, popup's dead-link pass, layout, theme sync, run
   band; (f) **`tabs` start** `tabs`:77-83 (`toFeatures()` if the hash names a scenario, bar height); (g) timeline and
   components starts (they read the filters as already applied, `tl`:115-116).
4. `parse_url_hash` (`hash`:103-174): splits on `&` and `=` (exactly two parts kept, `hash`:108-111), then applies `q`
   (runs the search, `hash`:112-115), `status` (:116-122), `depmode`/`catmode`/`tagmode` (:126-137), dependency chips
   (:148-149), tag chips and Happy paths (:150-153), `pctl` (:154-164), `dur` (:165-168), category chips (:169-170), and
   the anchor **last** (:173). Each filter call goes through `update_url_hash`, so loading rewrites the URL several times
   by `replaceState` (no entries). It is **additive**: it only turns on what the hash names and never clears anything,
   so it cannot be re-run on a traversal without a reset first.
5. Next task: `fbar` start (`fbar`:269-287).

Step (d) runs before (f): with a configured Timeline or Components start tab, the anchor is revealed while
`#panel-features` is still `hidden`, and only `jump_into_view`'s 10-frame re-aim (`hash`:55-64) moves the scroll once
(f) shows the panel.

### 2.3 Hash grammar (v2)

`#[<anchor>&]k=v&k=v…`, anchor first and kept by every rewrite (`hash`:96-99, `export`:31-35). Anchor = `scenario-<slug>`
or `sid-<16 hex>`. Keys written by `update_url_hash` (`hash`:71-102): `q` (encoded), `status` (comma list, not encoded),
`deps`, `depx`, `depmode`, `catmode`, `hp=1`, `hpx=1`, `cats`, `catx`, `tags`, `tagx`, `tagmode`, `dur`, `pctl` (encoded);
reserved chip values `__none__`, `__uncategorized__`, `__untagged__` (comment `hash`:66-70); a 4.x `(no interactions)`
dependency is read as `__none__` (`hash`:143). 4.x keys: `q`, `status`, `deps`, `depmode`, `catmode`, `hp`, `cats`, `dur`,
`pctl` (4.x `hash`:66-94).

## 3. Ids and addresses

### 3.1 In the page

| Thing | Written at | Shape |
|---|---|---|
| Scenario slug | `GenerateScenarioAnchorId` `RG`:4769-4774 | `scenario-` + lower-cased display name with every run of `[^a-z0-9]` turned into `-`, trimmed |
| De-duplication | `RG`:2058-2074 over every scenario in report order | 2nd and later get `-2`, `-3`… (positional) |
| Scenario element | `RG`:2416 | `<details class="scenario…" … data-stable-id="<sid>" id="<slug>" tabindex="0">` (the `data-stable-id` must stay before `id=`, `RG`:2408-2409, because cluster-link tests match `[^>]*id="…"` greedily) |
| Parameterized group | `RG`:3482, :3488 | `<details class="scenario scenario-parameterized…" … id="scenario-<group slug>">`, **not de-duplicated**, no `data-stable-id` on the group |
| Example row, grouped copy | `RG`:3678-3682 | `<tr … data-stable-id="<row sid>" id="<row slug>" data-scenario-id="<row slug>" onclick="selectRow(…)">` |
| Example row, flat copy | `RG`:3570-3575 | same `data-stable-id`, **no `id`** (flat table shown by default, `RG`:3493-3494, :3503; grouped copy hidden, `RG`:3592-3595) |
| Stable id | Reports/ScenarioStableId.cs:33 (Compute), :52 | 16 lower-case hex; computed for scenarios `RG`:2410, rows `RG`:3574, :3681, timeline rows `RG`:2176 |
| Timeline row | `RG`:2178 | `div.timeline-row[data-sid]` (no id, no link) |
| Feature | `RG`:2238 | `<details class="feature">`: **no id, no address**; rule `<details class="rule">` (`RG`:2296) no id |
| Tabs | `RG`:2861-2863, keys `RG`:1975-1979 | `button.report-tab#tab-<key>[role=tab][aria-controls=panel-<key>][data-tab=<key>]`; keys `features`, `timeline`, `components`, `history`, `diagnostics` |
| Panels | `RG`:2866-2867 (no-tab page: `RG`:2026) | `div.report-panel#panel-<key>[role=tabpanel][data-panel=<key>]`, `hidden` unless the start tab; without tabs, no `role` (so `activate` returns early, `tabs`:12) |
| Features views | `RG`:3046-3053; panels `RG`:2227-2231 | `button.view-btn#view-full|#view-summary[data-view]` controlling `#report-content` / `#features-table-view` |
| Other ids used by navigation | | `#filtering-box` `RG`:1828, `#filter-rows` :1832, `#searchbar` :1830, `#display-settings` :1997, `#features-sort` :3085, `#history-section` `HH`:149, `#scenario-timeline` `RG`:2154, `#component-diagram` :2219, `#back-to-top` :2605, `#failure-counter` :2601, diagrams `puml-<n>` :2201, popup segments `iflow-…` (`popup`:218-221) |
| Exposed globals a router could use | | `window.kronikol_activate_tab` (`tabs`:26), `window.kronikol_set_features_view` (`ftools`:30), `reveal_url_anchor`, `jump_into_view`, `current_url_anchor`, `update_url_hash`, `parse_url_hash`, `clear_all_filters`; nothing calls the two `kronikol_*` globals today |

### 3.2 Outside the page (the compatibility contract)

| Producer | Where | Exact shape |
|---|---|---|
| `kronikol query failures` (text) | src/Kronikol/Query/QueryCommand.Narrative.cs:117-118 | `  open: TestRunReport.html#sid-<id>`; prefix from `DeepLinkPrefix` :911-915 (`<data file name>.html#sid-`, only when that HTML sits beside the data file) |
| `kronikol query failures --json` | QueryCommand.Narrative.cs:170, :212 | `"deepLink": "TestRunReport.html#sid-<id>"` |
| `kronikol query steps` | QueryCommand.Narrative.cs:366-367 | `open: TestRunReport.html#sid-<id>` (the `query.cs` written beside a run runs the same engine) |
| Failures.md | src/Kronikol/Reports/FailuresDigestGenerator.cs:298 (shape), :715-716 | `· [open in the report](TestRunReport.html#sid-<id>)`, only when an HTML report is written |
| Failures.jsonl | FailuresDigestGenerator.cs:967 | `"deepLink": "TestRunReport.html#sid-<id>"` |
| CLAUDE.md / AGENTS.md beside a run | src/Kronikol/Reports/agent-instructions.md:89-90, :96-97; AgentInstructionsGenerator.cs:23, :26, :28, :38 | `__REPORT__.html#sid-<stableId>`, `__REPORT__` replaced by the report's file name |
| Labs page | `HH`:134-137 (via `LRG`:64, :108); `LRG`:83-84 | `<a class="history-link" href="TestRunReport.html#sid-<id>">`; `<a class="labs-back" href="TestRunReport.html">` (file name `Uri.EscapeDataString`'d, then HTML-encoded) |
| `kronikol merge` | same generators | `Combined.html#sid-…` on its labs page and digest |
| Skill script | templates/skills/kronikol-test-debugging/scripts/query.py:359-360 (and .claude/skills/kronikol-test-debugging/scripts/query.py:360) | `open: TestRunReport.html#sid-<id>` (file name hard-coded) |
| Doorstep check | .github/workflows/doorstep.yml:49, :52-59 | the published homepage links `reports/<x>/TestRunReport.html#sid-<16 hex>`, and each linked report must contain `data-stable-id="<id>"` |
| Wiki | ../Kronikol.wiki Generated-Reports.md:656-698 (wiki HEAD 450844e) | both forms documented, plus `TestRunReport.html#sid-6b1f0c2d3a4b5c6d&status=Failed&deps=Payments`, "the anchor is always the first segment", Clear All keeps it |
| The report itself | `RG`:2417, :3489 (🔗), `RG`:2111 (clusters) | `#scenario-<slug>`: what readers copy from the address bar and bookmark |
| CI summary | src/Kronikol/Reports/CiSummaryGenerator.cs | no fragment links into the report (its only links are PlantUML image URLs, :198-248) |
| PR report link action | templates/github-actions/kronikol-pr-report-link/action.yml:89, :102 | artifact zip URL `…/actions/runs/<run>/artifacts/<id>`, no fragment |
| History action | templates/github-actions/kronikol-history/scripts/*.sh | no report links |

## 4. Tests that pin this behaviour (v5)

No test anywhere calls `GoBackAsync`/`GoForwardAsync`, reads `history.length`, or asserts "no history entry" (grep of
`v5tests/` for `GoBack|GoForward|popstate|pushState|history.length`). The only pins on `replaceState` are source-text ones.

### 4.1 Core (tests/Kronikol.Tests)

| File | Fact (line) | Pins |
|---|---|---|
| Reports/UrlHashFilterTests.cs | `Filter_statuses_calls_update_url_hash` 64, `Filter_tags_calls_update_url_hash` 72, `Run_search_scenarios_calls_update_url_hash` 83, `Filter_dependencies_calls_update_url_hash` 91, `Filter_categories_calls_update_url_hash` 99, `Update_url_hash_saves_categories` 109, `Parse_url_hash_restores_categories` 120 | brace-matched bodies of `function filter_*(`, `function update_url_hash(`, `function parse_url_hash(`: must contain `update_url_hash()`, `parts.push('hp=1')`, `parts.push('hpx=1')`, `list('cats', chip_values('category', 'in'));`, `list('catx', …)`, `params.cats` |
| Reports/UrlAnchoredFilterReportTests.cs | `Report_contains_update_url_hash_function` 37, `…parse_url_hash_function` 45, `Report_updates_hash_on_filter_change` 53 (`history.replaceState` anywhere, :57), `Report_restores_filters_from_hash_on_load` 61 | names present |
| Reports/DeepLinkReportTests.cs | `Report_scenario_has_id_attribute` 37, `…anchor_is_derived_from_name` 45 (`id="scenario-create-order"`), `…handles_special_characters` 53, `Report_expands_scenario_from_hash_on_load` 61 (`parse_url_hash` contains `reveal_url_anchor(anchor)`; `reveal_url_anchor` contains `setAttribute('open', '')` and `jump_into_view(target, 'center')`; `jump_into_view` contains `behavior: 'instant'`, never `'smooth'`), `Report_scenario_has_anchor_link_button` 82 | slug ids, reveal internals |
| Reports/StableIdDeepLinkTests.cs | `Every_scenario_element_carries_its_stable_id` 104, `Every_example_row_carries_its_own_stable_id` 122, `An_outline_with_a_flat_view_carries_the_id_on_both_copies_of_each_row` 137 (flat table has no `id="scenario-`), `The_ids_in_the_markup_are_the_ids_in_the_data_file` 157, `The_specifications_report_carries_them_too` 177, `The_report_script_knows_how_to_resolve_a_sid_anchor` 186 | `data-stable-id` contract |
| Reports/ParameterizedGroupRenderTests.cs | `Rows_have_data_scenario_id_for_deep_linking` 487, `Deep_link_JS_handles_row_inside_parameterized_group` 502 (`reveal_url_anchor` contains `tr[data-scenario-id=`, `details.scenario-parameterized`, `.click()`), `Flat_table_rows_have_no_id_attribute` 1159 | row addresses |
| Reports/ClearAllFiltersReportTests.cs | `Clear_all_function_clears_the_filters_and_keeps_the_anchor` 66 | exact `var keptAnchor = current_url_anchor();` and the exact `history.replaceState(null, '', window.location.pathname + window.location.search + (keptAnchor ? '#' + keptAnchor : ''))` (:72-73) |
| Reports/FailureClusterReportTests.cs | `Report_cluster_link_href_matches_scenario_element_id` 110, `Report_cluster_link_onclick_uses_correct_anchor_id` 135, `…href_matches_element_id_for_parameterized_scenarios` 156, `…onclick_opens_ancestor_details_for_parameterized_row` 200 (`el.click()`), `…onclick_jumps_into_view` 239 (`jump_into_view(el,'start')`), `…onclick_prevents_default_for_reliable_scrolling` 259 (`event.preventDefault()`) | the inline onclick of F2 |
| Reports/ReportTabsTests.cs | `The_bar_opens_with_a_tab_for_each_section_and_features_selected` 65 (exact tab markup, helper :58-59), `Each_section_is_in_its_own_panel…` 79 (panel markup :61-62), `A_panel_configured_visible_is_the_tab_the_page_opens_on` 127-141, `A_page_with_nothing_beside_its_features_has_no_tabs` 144, `The_specifications_report_keeps_its_one_view` 158, `The_tabs_script_is_in_the_page` 166 (`<script id="kronikol-report-tabs">` holds `aria-selected`, `_renderDiagramsInContainer`, `'ArrowRight'`, `--kron-nav-h`) | tabs are `<button>`s; changing them to links breaks these exact strings |
| Reports/FeaturesToolsTests.cs | `Full_and_Summary_are_tabs_with_Full_selected` 90 (exact view buttons), `FeaturesSummaryOpen_opens_the_page_on_the_Summary_view` 111 | view markup |
| Reports/PersistentFilterStateReportTests.cs | `Report_stores_nothing_but_the_theme` 38 (every `localStorage.x(` names `'kronikol.theme'` and no other `localStorage` mention), `Report_does_not_call_restore_filter_state_on_load` 52, `Report_still_parses_url_hash_on_load` 61 | a router may not keep state in `localStorage` (`sessionStorage` is not checked) |
| Reports/DefaultReportCleanTests.cs | `A_run_that_read_no_history_and_recorded_no_diagnostic_names_neither_anywhere_in_the_file` 112-122, `Turned_off_the_history_and_diagnostics_tabs_leave_no_trace_of_either` 125 | the substring `history-` may not appear anywhere in such a file: router code must not contain `history-` and must take tab keys from the DOM (the tabs script "names no section", optionc.md:267-268) |
| History/HistoryHtmlTests.cs | `The_section_is_the_history_tabs_panel_open_with_its_summary_out_of_the_tab_order` 119, `The_history_section_lists_the_regression_and_links_it_by_stable_id` 172 (`href="#sid-<id>"`, :182) | F3 href |
| Reports/LabsReportTests.cs | `The_names_link_into_the_report_by_stable_id_and_the_page_has_no_script` 113 (exact `<a class="history-link" href="TestRunReport.html#sid-<id>">…</a>`, :117), `A_report_named_otherwise_is_linked_by_its_own_name` 124, `The_table_is_collapsed_and_lists_every_scenario_in_report_order_under_its_feature` 166, `Each_outline_row_is_a_row_of_its_own_with_its_own_stable_id` 184, `The_header_names_the_report_the_run_and_the_way_back` 267 (`<a class="labs-back" href="TestRunReport.html">`, :277) | labs links |
| Reports/LabsReportRunTests.cs | `With_no_option_set_a_run_that_read_history_and_recorded_a_diagnostic_writes_the_page_beside_its_report` 107 (:119), `The_page_is_named_after_the_report_and_links_to_it_by_that_name` 124 (:131-132) | labs links |
| Reports/Merge/MergeCommandTests.cs; Reports/Merge/MergeWritesTheRunOutputsTests.cs | `Merge_with_a_ledger_puts_history_on_the_labs_page_and_never_writes_to_it` 184 (:229); `Merge_writes_the_failures_digest_beside_the_report` 37 (:49) | `Combined.html#sid-` |
| Reports/DigestDeepLinkGatingTests.cs; Reports/FailuresDigestGeneratorTests.cs | `A_digest_written_without_the_html_report_links_to_nothing` 60, `A_digest_written_beside_the_html_report_still_links_to_it` 80 (:88-96); `Each_failure_carries_its_stableId_and_a_deep_link` 108 (:114) | Failures.md / .jsonl link |
| Tool/QueryCommandTests.cs; Tool/RetainedRunsTests.cs | `Failures_hands_back_a_link_that_opens_the_scenario_in_the_report` 193 (:203), `Failures_offers_no_link_when_the_html_was_never_generated` 207, `Steps_offers_the_same_link_next_to_the_stable_id_it_already_prints` 218 (:225); `Run_opens_a_retained_run_on_any_verb_and_its_siblings_are_found_beside_it` 91 (:106) | `open: TestRunReport.html#sid-` |
| Reports/AgentInstructionsGeneratorTests.cs | `It_teaches_the_ladder_and_the_addresses` 57 (:65) | `TestRunReport.html#sid-<stableId>` |
| Reports/KeyboardNavigationReportTests.cs | facts 37, 46, 55, 63, 73 | arrow keys, Enter, `/`, `tabindex` on scenarios |
| Reports/ScenarioImageLightboxTests.cs | `A_scenarios_image_a_rows_image_and_a_steps_image_each_open_in_the_lightbox` 26 (:34) | exact `<a class="attachment-image-link" href="attachments/<name>" onclick="openLightbox(event, this)">` |
| Reports/TimelineControlsTests.cs | `Each_row_names_its_scenario_and_carries_its_times_as_the_scenarios_element_writes_them` 122, `Two_scenarios_of_one_name_in_two_features_are_two_rows_each_with_its_own_id` 165 | `data-sid` |

### 4.2 Playwright (tests/Kronikol.Tests.EndToEnd; every page is opened from `file://`, ReportTestHelper.cs `new Uri(path).AbsoluteUri`)

| File | Fact (line) | Pins |
|---|---|---|
| StableIdDeepLinkTests.cs | `A_sid_link_opens_the_scenario_it_names_and_not_its_namesake` 125, `Filtering_after_following_a_link_keeps_the_link_in_the_url` 145 (hash starts `#sid-<id>` after a filter, :159-163), `A_link_that_carries_filter_state_applies_both` 168, `Clearing_every_filter_keeps_the_link_but_drops_the_filters` 184 (hash equals `#sid-<id>`, :197-200), `A_link_pasted_into_an_open_report_still_resolves` 204 (`location.hash = 'sid-…'` → H3, :216), `A_sid_link_to_an_example_row_opens_the_group_and_selects_the_row` 224, `A_link_to_a_row_lands_on_the_copy_that_is_actually_displayed` 243, `The_slug_anchor_for_an_example_row_now_opens_its_group_too` 264, `A_sid_link_far_down_a_long_report_lands_on_its_scenario` 280, `At_a_phone_width_a_sid_link_lands_on_the_page_the_folded_filters_leave` 297 | anchor-first rule, reveal on load and on `hashchange` |
| UrlHashFilterHighlightTests.cs | load restores 59, 74, 94, 108, 229; click then `ReloadAsync` 120, 141, 164, 187, 207 (`pctl=`) | filters round-trip through the hash |
| FilterPipelineTests.cs | `The_link_carries_every_filter_and_a_fresh_page_reads_it_back` 234 (`tags=api`, `tagx=smoke`, `tagmode=AND`, `depx=Payment%20Service`, `catx=Fast`, `hpx=1`, :245-250), `A_link_from_a_4x_report_still_reads` 268, `A_qualifier_narrows_the_search_and_stays_in_the_link` 293 | v2 grammar |
| FilterModeDefaultsTests.cs | `Or_dependency_mode_default_seeds_button_and_hash_grammar` 42, `And_category_mode_default_seeds_button_and_hash_grammar` 94 | mode keys |
| ReportTabsTests.cs | `A_tab_shows_its_panel_alone_and_takes_the_tab_order` 33, `The_arrow_keys_home_and_end_move_between_the_tabs` 53, `The_component_diagram_is_drawn_when_its_tab_first_shows` 73, `Next_failure_from_another_tab_goes_to_the_features_tab_and_the_failure` 83, `A_history_link_goes_to_the_features_tab_and_its_scenario` 95 (URL contains `#sid-`, :104), `A_scenario_address_opens_on_the_features_tab_even_when_another_tab_was_showing` 108 (`location.hash = <id>`), `The_history_and_diagnostics_sections_stay_open_in_their_tabs` 120 | tab behaviour; no URL for tabs |
| FeaturesToolsTests.cs | `Full_and_Summary_show_the_cards_or_the_table…` 62, `The_views_move_with_the_arrow_keys_Home_and_End` 86, `A_row_opens_its_feature_in_the_Full_view_from_a_click_or_the_keyboard` 158, `Next_Failure_from_the_Summary_view_goes_to_the_failure_in_its_card` 190, `On_a_phone_the_gear_shows_and_hides_the_display_settings` 202, `Returning_to_the_Features_tab_draws_only_the_diagrams_that_show` 225 | views, row→card |
| HistorySectionTests.cs | `A_link_in_the_section_opens_the_scenario_it_names` 47 (URL ends `#sid-<id>`, :61) | F3 |
| LabsPageTests.cs | `A_scenario_named_on_the_page_opens_in_the_report` 63 (`WaitForURLAsync(report + "#sid-…")`, :71), `The_page_runs_no_script` 80 | F10; the labs page must stay script-free |
| FailureClusterLinkTests.cs | scrolling and opening facts 248-669, incl. `Cluster_link_updates_url_hash` 498 (URL contains `#scenario-…`, :508), `Cluster_link_to_parameterized_row_scrolls_to_visible_row` 606 (fixture `CreateParameterizedClusterData` :129-161 has no `ExampleFlatValues`) | F2 |
| JumpToFailureTests.cs | 9, 32, 48, 75 | B5 |
| ParameterizedGroupTests.cs; ScenarioInteractionTests.cs | `Scenario_link_exists_on_parameterized_group` 291 (href has `#scenario-`), `Deep_link_hash_opens_parameterized_group_and_selects_row` 308; `Scenario_link_exists_on_each_scenario` 106, `Scenario_link_href_points_to_scenario_id` 119, `Context_menu_dismissed_by_escape_key` 305 | F1 href |
| TimelineTabTests.cs; ComponentsTabTests.cs; DeepSearchTests.cs | `A_filter_in_the_pages_link_reaches_the_timeline_as_it_opens` 159; `A_links_filters_reach_the_first_drawing` 275; `Hash_restore_applies_deep_results_once_the_index_is_ready` 492 | load-order dependants |
| FilterBarTests.cs | `More_filters_opens_and_folds_the_rows_on_a_desktop` 78, `The_search_help_opens_while_the_rows_are_folded` 121, `Filters_shows_while_the_bar_floats_and_takes_the_box_into_it` 162 (Esc :181), `The_box_goes_back_when_the_bar_comes_to_rest` 197, `No_scenario_left_shows_the_empty_state_whose_Clear_All_returns_to_the_search` 210 | E1-E5, D5 |
| IflowPopupTests.cs; AttachmentRenderingTests.cs; GradualZoomTests.cs | `Close_button_removes_overlay` 56, `Escape_key_closes_popup` 68, `Clicking_overlay_background_closes_popup` 80, `Opening_new_popup_replaces_existing_one` 481; lightbox Esc 112, 212, `Non_image_attachment_renders_as_link_not_img` 255; `Escape_deselects_diagram` 69 | layer closing |
| ScenarioSettingsTests.cs | `The_gear_opens_and_closes_the_scenarios_settings_and_stays_where_it_was` 37, `On_a_phone_the_gear_on_the_sections_title_opens_the_settings_without_folding_the_section` 133 | C3, C4 |
| ThemeTests.cs; MobileResponsiveTests.cs; FormStateRestorationTests.cs; OpenInNewTabTests.cs | `The_switch_flips_the_theme_and_the_pick_is_kept_over_a_reload_and_the_system` 74; back-to-top 382, 393, 417; reload form state 96, 116; new-tab items 99, 109, 118 | E6, B6, reload restoration, G4 |

## 5. 4.x (origin/main 17914701) against v5

| Piece | 4.x | v5 | Where a fix can land |
|---|---|---|---|
| Sections | No tabs. Scenario Timeline and Component Diagram are mutually exclusive toggled panels above the features (`toggle_timeline` / `toggle_component_diagram`, 4.x `RG`:1398-1428, buttons 4.x `RG`:1879, :1881); History section inline above the features (4.x `RG`:2007-2008) and Report diagnostics inline (4.x `RG`:1994-1995), **both off by default** (4.x ReportConfigurationOptions.cs:494, :859 have no initialiser) | Tabs (`tabs`), History and Diagnostics tabs **on by default** (v5 ReportConfigurationOptions.cs:494, :859 `= true`) | Tab routing is v5 only. In 4.x the default home of history is the labs page, whose links are cross-document, so Back already works there; the in-page History link exists only when opted in |
| Features views | "Features Summary" `<details>` (4.x `RG`:1631); rows not clickable | Full/Summary switch, row opens its card, sort select, fold groups | v5 only |
| Expand/collapse | text buttons `toggle_expand_collapse` (4.x Reports/report-collapse-expand-all-function.js:1-6, 4.x `RG`:1871) | fold groups (`ftools`) | |
| Hash | v1 keys; same anchor-first rule and `replaceState` (4.x `hash`:66-94), same additive `parse_url_hash` (4.x `hash`:95-166); `hash`:1-65 byte-identical | v2 | anchor/popstate/scroll work fits both |
| hashchange | `init` only (4.x `init`:6-9) | H1-H3 | |
| In-page links | failure-cluster onclick identical (4.x `RG`:1982); 🔗 identical (4.x `RG`:2265, :3054); History `InPageLink` identical (4.x `HH`:130-134); Clear All identical (4.x `export`:41-42) | same | pushing an entry for F1-F3 and restoring on traversal could ship in 4.x too |
| Jump, lightbox, copy name, select row | byte-identical scripts | same | |
| Keyboard | same, minus the `SELECT` and Enter-only-on-body guards | | |
| Search clear closes all | yes (4.x `search`:42, :128) | yes (`search`:134, :220) | |
| Phone | init builds "⚙ Diagram Settings" toggles (4.x `init`:33-49); mobile Filters toggle (4.x `RG`:1773) | gears (`settings`, `ftools`) | |
| Filter bar, More filters, empty state, chips, theme, timeline/components controls | absent | present | v5 only |
| Tests | UrlHashFilterTests, UrlAnchoredFilterReportTests, DeepLinkReportTests, StableIdDeepLinkTests (both), ClearAllFiltersReportTests, FailureClusterReportTests, HistoryHtmlTests, LabsReportTests, UrlHashFilterHighlightTests, HistorySectionTests, LabsPageTests, FailureClusterLinkTests exist on 4.x | ReportTabsTests, FeaturesToolsTests (both), FilterPipelineTests, FilterBarTests are v5 only | |

## 6. Surprises and bugs (reasoned from code unless stated)

| # | Finding | Where | Confidence |
|---|---|---|---|
| S1 | **The owner's bug.** History tab links (and failure-cluster links, Next Failure, a Summary row) make no history entry, and the tab is in no URL, so Back can never return to the History tab or the place on it | `HH`:143 (`preventDefault` + `replaceState`), `RG`:2111, `jump`:2-16, `ftools`:80-92, `tabs`:10-25 | high |
| S2 | In-page links drop the filter state from the URL while the page keeps the filters: the cluster and History handlers `replaceState` the bare anchor, and the 🔗 `href` is the bare anchor. The URL stops reproducing the view until the next filter change rewrites it from the DOM | `RG`:2111, `HH`:143, `RG`:2417, :3489; contrast `hash`:96-99 and `export`:34-35 which keep both | high |
| S3 | `hashchange` never re-applies filters, so any traversal between entries whose filter parts differ (or a pasted `#status=Failed`) leaves URL and page disagreeing. `parse_url_hash` cannot simply be re-run: it is additive (only turns on what the hash names; `q` only when non-empty) | `init`:2-9; `hash`:112-170 | high |
| S4 | On a same-document traversal the browser restores the entry's scroll, then `hashchange` runs H3, which re-reveals the entry's anchor and overrides the restored position (HTML spec: popstate, restore persisted state, then queue hashchange). A future `popstate` handler would run before the scroll restore | `init`:6-9 | medium (spec order; verify in Chromium, Firefox, WebKit) |
| S5 | On load the anchor is revealed before the tabs script shows the Features panel (`DOMContentLoaded` registration order), so with a Timeline/Components start tab the first aim hits a hidden element and only the 10-frame re-aim rescues it. The `hashchange` order (H1, H2, H3) is the safe one; load runs `ftools` start, then `init`, then `tabs` start | `init`:1, :37-39; `tabs`:84; `hash`:55-64 | high (fragile, works today) |
| S6 | A failure-cluster link to an example row of an outline **with a flat view** targets the hidden grouped copy (the only one with an `id`): `el.click()` selects the hidden row, the visible flat table keeps its old active row, and `jump_into_view` aims at an element with no box, so nothing scrolls. `reveal_url_anchor` fixed exactly this for `#sid-`/`#scenario-` (re-resolve through `data-stable-id`), the inline cluster handler never got it. Uncovered: the cluster tests' parameterized fixture has no `ExampleFlatValues` | `RG`:2111; `RG`:3493-3494, :3503, :3592-3595; `hash`:30-35; FailureClusterLinkTests.cs:129-161 | medium-high |
| S7 | A link to a scenario the current filters hide does nothing visible: the handlers open it and jump to an element with `display:none`; `element_for_stable_id` falls back to a hidden copy. E.g. `status=Failed` on, then a "Newly fixed" History entry: the page switches to Features and shows nothing, with no message | `map`:40; `hash`:15-23, :45; `RG`:2111 | high |
| S8 | Emptying the search closes every scenario, including the one a link opened, while the URL still names it | `search`:217-221 (also :131-135; 4.x :42, :128) | high |
| S9 | Parameterized group ids are not de-duplicated (scenario ids are), so two features with same-named groups, or a group named like a scenario, give duplicate `id`s and `#scenario-<slug>` resolves to the first | `RG`:3482 vs `RG`:2058-2074 | high |
| S10 | `#features-sort` is the only form control without `autocomplete="off"`; a reload or a history traversal that reloads the page can restore its value without a `change` event (Firefox does), so the select would name an order the cards do not have. FormStateRestorationTests covers only the note selects (96, 116) | `RG`:3085 vs `RG`:1089, :1225, :1554, :1557, :1830, :1892 | medium |
| S11 | ArrowDown/ArrowUp with a report tab focused: `tabs` returns before `toFeatures`, but `keys` still moves `.scenario-focused`, scrolls inside the hidden Features panel and opens a feature, unseen | `tabs`:55-61; `keys`:14-29 | medium (low impact) |
| S12 | Stale comments: `HH`:139 says "the hash handler opens the scenario" but the link cancels navigation and calls `reveal_url_anchor` itself (no `hashchange`); `tabs`:47 lists "a diagnostic's" link, but diagnostics name scenarios as text | `HH`:139-143; `tabs`:47-48; `RG`:4806-4807 | high |
| S13 | No per-tab scroll memory: all panels share the window's scroll, and `activate` neither saves nor restores it | `tabs`:10-25 | high |
| S14 | The 🔗 writes the weak slug address (moves on rename, positional `-2` suffix) while every outside producer writes `#sid-`; the 📋 next to it copies only the name. There is no "copy link" action | `RG`:2417, :3489; Reports/report-copy-scenario-name-function.js:4 | high |
| S15 | Same "names a scenario" predicate implemented three times; a new address form (e.g. a tab or view key) needs three edits | `hash`:7-8, `tabs`:36-37, `ftools`:175-176 | high |
| S16 | Two divergent reveal paths: the cluster handler re-implements opening and jumps to `'start'` without row re-resolution, the History link calls `reveal_url_anchor` (`'center'`) | `RG`:2111 vs `HH`:143, `hash`:25-46 | high |
| S17 | Layers (lightbox, internal-flow popup, context menu, gears, search help, box in the bar) have no history integration; on Android the system Back leaves the report with the layer open. Esc is handled separately by four listeners (`lightbox`:13, `popup`:257-262, `ctx`:891-893 and :927-929, `fbar`:240-242) | as cited | high |
| S18 | Design constraints a router must keep: no `localStorage` beyond the theme (PersistentFilterStateReportTests:38-49); no `history-` substring in a report without history (DefaultReportCleanTests:112-122, :125); tab keys read from the DOM, not hard-coded (optionc.md:267-268); the labs page runs no script (LabsPageTests:80); the exported Filtered HTML keeps the head scripts with no tabs in the body (`export`:103-118), and `activate` already tolerates a page without tabs (`tabs`:12) | as cited | high |
| S19 | `file://`: every E2E test opens reports from `file://` and `replaceState` works there; the spec's "can have its URL rewritten" rule rejects only a changed path for `file:` URLs, so fragment-only `pushState` should be allowed. Back/forward-cache behaviour for `file://` and a page with an opener (`ctx`:253 opens `window.open('', '_blank')` without `noopener`) is unverified | `ctx`:252-254 | research item |
