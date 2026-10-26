# The title line, the filter box, the bar and the tokens: a line map (2026-10-26, 29e551f3)
RG = src/Kronikol/Reports/ReportGenerator.cs, CSS = src/Kronikol/Reports/stylesheets.css, FB = report-filter-bar-script.js,
TOK = src/Kronikol/Reports/report-tokens.css.

## Top 4
- Search button can't share .theme-toggle class: report-theme-switch-script.js:37-39 flips theme on ANY click inside
  .closest('.theme-toggle'); sync() updates only first .theme-toggle (15-21); E2E locator `button.theme-toggle`
  (ThemeTests.cs:14). -> own class, shared look via a new selector.
- Only live region (span.filter-readout[role=status]) is INSIDE the box; bar copy aria-hidden (RG:3128; comment RG:3123 "the
  box's readout announces for it"). Hidden box silences announcements -> need a live region outside the box.
- On a phone and on pages without tabs the only Filters button is inside the box -> new control needed in both.
- ~25 test files + 3 helpers expect visible box or visible #searchbar.

## 1. Title line
- RG:1763-1766: custom-logo div (optional) then `<div class="title-line"><h1>…</h1>{ThemeSwitchButton}</div>`, first in body
  on TestRunReport + Specifications. Then run band (TestRunReport only RG:1771, 1882-1886), box (1923-1924), nav mark + bar
  (2044-2045), panels. Switch RG:92-100: button.theme-toggle aria-label="Dark theme" aria-pressed="false" title="Switch to
  the dark theme", svg.theme-icon.theme-moon/.theme-sun 24x24 viewBox aria-hidden; NO id.
- Labs: LabsReportGenerator.cs:62-67 header.labs-header > div.title-line > h1 (span.labs-badge) + ThemeSwitchButton, then
  p.labs-about, p.labs-meta.
- CSS:16-32: .title-line flex, align center, space-between, gap 12px, margin 4px 0 14px (18); h1 min-width 0,
  var(--kron-fs-title) 22px, weight 600, letter-spacing -0.01em (19-21); .theme-toggle (22-26) flex none, inline-grid,
  place-items center, 34x34, padding 0, border 1px solid var(--kron-hairline), radius 8px, bg var(--kron-card), color
  var(--kron-ink-soft), box-shadow var(--kron-card-shadow); hover --kron-hover/--kron-ink (27); focus outline 2px solid
  var(--kron-accent) offset 2px (28); .theme-icon 18x18, fill none, stroke currentColor, stroke-width 1.8, round caps (29);
  swap on :root[data-theme="dark"] (30-32). No :active/[aria-pressed] style; no .custom-logo rule; no title-line breakpoint
  rule. Body padding 14px 18px 60px (CSS:8); padding-inline 16px at <=480 (2168-2171); overflow-wrap anywhere (CSS:12).
- space-between would centre a 3rd child -> right-hand wrapper group or margin-left:auto.
- Pins: ReportThemeTests.cs:91-100 exact h1+toggle string; ThemedPagesHtmlTests.cs:107-126 labs title-line children
  ["h1","button"], header ["div","p","p"]; BackForwardPlacesTests.cs:38,341 + BackForwardRestoreTests.cs:33 click h1.
- Export page: only <h1>Filtered Report</h1> (report-export-function.js:132).
- Wiki: Generated-Reports.md:94-95; Report-Themes.md:9, 29-30; Migrating-to-v5-Report.md:16.

## 2. Theme
- Boot report-theme-boot-script.js:1-9 localStorage 'kronikol.theme' (try/catch) else prefers-color-scheme ->
  data-theme; first in head <script id="kronikol-theme-boot"> (RG:1707, loaded 1663); compressed loader RG:2729; labs :46.
- Switch report-theme-switch-script.js: picked (8), stored() 9-13, current() 14, sync() 15-21 (aria-pressed + title; label
  stays "Dark theme"), set(t, keep) 22-29, fallback 30-31, follows system while nothing stored 32-36, delegated click 37-39,
  sync at DOMContentLoaded 40-41; <script id="kronikol-theme-switch"> RG:1755; labs :57.
- TOK: light :root 6-122; dark :root[data-theme="dark"] 123-226; @media (prefers-color-scheme: dark)
  :root:not([data-theme]) 227-332 must equal dark block (comment 1-5; ReportThemeTests.cs:70-83); `[hidden] { display:
  none !important; }` at 333. Loader --kron-loading-* (report-loader.css:5-28).
- Storage: PersistentFilterStateReportTests.cs:37-49 fails any localStorage mention not naming 'kronikol.theme'
  (OPTION_C Q1 :157-163; wiki Report-Themes.md:12-24). State only in URL hash + session-history entries.
- Theme tests: ReportThemeTests.cs:57-124; E2E ThemeTests.cs:14, 58-152; CompressedReportTests.cs:322.

## 3. Tokens (TOK; light line N 8-64, dark N+117 125-181), light / dark
- Surfaces: --kron-hover 13 #edf1f8/#222b38; --kron-ground 14 #ebeef4/#0e131a; --kron-card 15 #ffffff/#171d26;
  --kron-strip 16 #f8fafd/#1b222c; --kron-strip-edge 17 #d3d9e3/#323c4b; --kron-field 18 #f2f5f9/#1f2732; --kron-quiet 19
  #f7f9fc/#1b222c; --kron-quiet-edge 20 #d3d9e3/#323c4b; --kron-panel 30 #f4f6fa/#12181f; --kron-code-bg 60 #e9edf3/#262f3c;
  --kron-track 61 #edf0f5/#232b37; --kron-overlay 64 rgba(0,0,0,.4)/rgba(0,0,0,.6).
- Edges: --kron-hairline 21 #d0d7e2/#343e4d; --kron-hairline-soft 22 #e2e7ee/#262f3b; --kron-divider 23 #cdd4df/#384354;
  --kron-divider-soft 24 #dde2ea/#2b3440; --kron-control-border 25 #aeb7c6/#4a5568; --kron-select-border 26 #7a8496/#748093;
  --kron-dot 27 #8a94a5/#7d899b.
- Shadows: --kron-shadow 28 rgba(20,30,60,.06)/rgba(0,0,0,.45); --kron-card-shadow 29 `0 1px 2px rgba(20,30,60,.07), 0 2px
  6px rgba(20,30,60,.05)` / `0 1px 2px rgba(0,0,0,.5), 0 2px 8px rgba(0,0,0,.3)`.
- Ink: --kron-ink 31 #242b36/#e4e9f0; --kron-ink-soft 32 #4a5361/#c5cdd8; --kron-ink-quiet 33 #545c6a/#b3bcc8; --kron-muted
  34 #5b6474/#a0aab7; --kron-label 35 #5f6878/#9ba5b3; --kron-label-soft 36 #616a7a/#98a2b0; --kron-faint 37 #626b7a/#949eac;
  --kron-on-solid 38 #fff/#fff; --kron-on-accent 39 #fff/#10161e.
- Accent: --kron-accent 8 #2559c7/#7ea6f2; --kron-selected 9 #dce8fb/#1f3254; --kron-selected-ink 10 #1d4fb0/#b0c9fb;
  --kron-selected-edge 11 #6a8fd9/#5b82cf; --kron-accent-rail 12 rgba(37,89,199,.18)/rgba(126,166,242,.30).
- Status: pass 40-42 --kron-pass #276c44/#62cd8f (+-soft, -ring); fail 43-48 --kron-fail #b3372e/#f28b81 (+-soft, -edge, -ink,
  -shadow, -solid); exclude 49-51; exception-soft 52; warn 53-55 --kron-warn #8a5a17/#e5b066 (+-soft, -edge); skip 56-57
  --kron-skip #5b6170/#a9b1be (+-soft); bypass 58-59 --kron-bypass #285fba/#8ab1f6 (+-soft); props 62-63.
- Type (light block 113-121): --kron-font, --kron-mono; --kron-fs-label 11px, -meta 12px, -control 13px, -body 14px,
  -feature 16px, -title 22px; --kron-label-track .06em. Diagram --dg-* light 66-111, dark 183-225.
- NO tokens for spacing, radii, control heights, z-index. Fixed values: control height 28px for every boxed control (comment
  CSS:490-492); theme toggle 34px (23); <=480 touch 36px (2183, 2202). Radii: 12px cards (200, 788, 902, 934); 8px toggle
  (24), More filters (275), iflow popup; 6px bar buttons (880, 894, 906); 5px export/fold (830); 999px pills (285, 866); 3px
  kbd (390-397). Focus ring: outline 2px solid var(--kron-accent) offset 1px (281, 819, 886, 920, 2000, 2234); offset 2px on
  toggle (28); -3px on tabs (799). Shadows: card-shadow on cards + toggle; `0 2px 8px var(--kron-shadow)` pinned bar (926);
  `0 4px 12px var(--kron-shadow)` back-to-top (2030). Motion .12s/.15s; only reduced-motion rule CSS:294.
- z-index: 1 chips/segments; 4 .scenario-settings (collapsible-notes-styles.css:104); 10 diagram zoom
  (inline-svg-styles.css:79); 40 bar (CSS:787); 100 phone .filter-search (CSS:2053); 1000 jump-to-failure + back-to-top
  (CSS:662, 2019); 10000 lightbox (CSS:1160); 20000 .iflow-overlay (internal-flow-popup-styles.css:5); 20001/20002 context
  menu + submenu (context-menu-styles.css:9, 50); 30000 toast inline (context-menu-script.js:686).
- Colour rule: ReportSheetColoursTests.cs:84-93 fails any literal colour not on allow-list 34-43 (lightbox scrim + image
  shadow, jump-to-failure hover, context menu + submenu shadows, .iflow-popup shadow, warm-up hatch). New overlay: scrim
  var(--kron-overlay), shadow from --kron-shadow (or extend allow-list).
- Violet (Specifications default, Constants/Stylesheets.cs:58-165; ReportConfigurationOptions.cs:64-66): overrides accent,
  hover, selected, field, strip, pass, bypass light (64-82) + dark (83-101, 102-122); `.feature, .features-table-view,
  .filtering-box { background-color: var(--violet-card); }` (124); phone .filter-search (125-127); `#searchbar {
  border-color: var(--kron-selected-edge); }` (132).
- #searchbar CSS:249-268: padding .5em; radius .5em; width 100%; border 1px solid var(--kron-select-border); bg
  var(--kron-field); color var(--kron-ink); font-size var(--kron-fs-body); placeholder --kron-faint; :focus bg card, border
  accent, outline 2px accent offset 1px; .kron-q-bad --kron-warn border; phone 16px (2165). Only kbd style:
  .search-help-note kbd (CSS:390-397) code-bg, control-border, radius 3px, padding .1em .4em.

## 4. Filter box
- RG:1924 only seed of rows-folded; no option/toggle default controls box/rows/search/help (ReportToggleDefaults.cs:15-137,
  ReportToggleDefaultsResolver.cs:9-50); only TagFilterMode/DependencyFilterMode touch filters (RG:1489-1490, 1500-1502);
  search option only FullSearchIndex (ReportConfigurationOptions.cs:100-107).
- Header 1925; search line 1926; phone line 1927; #filter-rows 1928; help 1929-1945 (note 1944 "Press <kbd>/</kbd> to focus
  the search bar"); rows 1946-2029 (status 1957-1966, duration 1970-1991, happy 1994-1995, tags 2002-2014, deps 2017-2027).
  Helpers: FilterRowsTitle 3062-3067, ClearAllTitle 3069-3075, FiltersLeaveShowing 3107, SearchTitle 3110, SearchHelpTitle
  3113, MoreFilters 3115-3120, FiltersClose 3130-3132, FiltersEmpty 3134-3136.
- CSS: card 194-204 (container-type inline-size 203); header 206-233; .filters 235-239; .filter-search 241-247; #searchbar
  249-268; .search-field 272; .more-filters + count 273-289; fold rules 290-293:
  `.filtering-box.rows-folded:not(.help-open) > .filters { display: none; }`
  `.filtering-box.rows-folded.help-open > .filters > :not(.search-help-panel) { display: none; }`
  `@media (max-width: 768px) { .more-filters { display: none; } }`
  container queries <=520, <=430 (hides .wide-only), <=300 (574-596); riding 887-892; phone 1981-2011, 2044-2068, 2165;
  <=480 2183, 2202-2210.
- JS: delegated fold handler FB:330-338; moreFilters() FB:56-83 (on() 63-69; paint 70-79; repaint capture 81 + on
  kron-filter-change 329); measureNeeds gives up while rows hidden FB:33-39; ResizeObserver 320-325; toggle_search_help
  report-search-function.js:3-9; placeReadout FB:265-275; empty-state/filtered-note focus search only if rendered
  (FB:296-301, 304-310).
- Live region: filter_readout (report-scenario-feature-map-helper.js:93-130) writes box's .filter-readout[role=status]; bar
  copy aria-hidden (RG:3128).

## 5. Nav bar
- RG:2044 nav-mark (only with tabs); 2045 toolbar-row report-nav [on-features][features-table-mode] or plain
  `<div class="toolbar-row{tableMode}">` without tabs; 2049-2056 div.nav-row > div.report-tabs[role=tablist][aria-label=
  "Report sections"]; 2057 NavFilters. Bar only when useTabs = hasDurations || hasComponentTab || hasHistoryTab ||
  hasDiagnosticsTab (RG:1677-1685); without tabs: no .nav-filters, no Filters button, no focus script (RG:1686-1690),
  bar() never runs (FB:294, 313).
- NavFilters RG:3122-3128 (exact markup: nav-filters role=group aria-label=Filters; nav-readout aria-hidden; nav-clear
  hidden; nav-filters-toggle aria-expanded=false aria-controls=filtering-box title="Show the filter box in this bar, so it
  stays in view as you scroll"; funnel svg 14x14 + span Filters).
- CSS: bar 786-789 sticky, z 40, card bg, hairline, radius 12px, card shadow; tabs 790-801; row + group 857-886; toggle 879-885
  (.is-active, [aria-expanded=true]); readout + Clear hidden while riding 887-888; box in bar 891; has-filters 892; .nav-mark
  921; floating rule 922-927:
  `@media (min-width: 768.02px) { .toolbar-row.report-nav:not(.k-floating) .nav-filters-toggle { display: none; }
   .toolbar-row.report-nav.is-pinned { border-radius: 0; box-shadow: 0 2px 8px var(--kron-shadow); } }`
  phone `@media (max-width: 768px) { .nav-filters { display: none; } }` (929); position static (2149); desktop bar joins
  panel (930-937).
- bar() FB:88-263: home = above bar (94); fit() 99-107 (tabs.scrollWidth + group.offsetWidth + 24) runs from hooks.sync (152),
  tab clicks (239), resize (260), NOT on k-floating change; hooks.sync 138-154; capBox 180-187; open() 188-211 (returns on
  riding || phone; nav.firstChild; has-filters, riding; close shown; aria-expanded true; title 'Put the filter box back above
  the bar'; focuses #searchbar); shut() 212-228; Esc 236-238; scrolled() 245-253 (is-pinned, k-floating, shut when at rest);
  resize 260.
- Toggle today: phone hidden; desktop at rest hidden; floating shown; riding shown selected; page without tabs absent.

## 6. Phone / bands
- One breakpoint max-width 768px / min-width 768.02px (StylesheetRulesTests.cs:77-126; :125 requires FB:12 string); no
  empty media blocks (236-242). 768.02-1160 band GONE since 5.0.0 S2 (OPTION_C :228-252; StylesheetRulesTests.cs:57-64);
  ViewportSweepTests.cs:52-60 keeps 1161 as a sweep width. <=480 band CSS:2168-2211 (padding, 36px touch, ellipsis chips).
- Media blocks CSS: 293, 294, 849, 922, 929, 930, 2037, 2168, 2242; container 574, 585, 594; others: collapsible-notes-styles
  9, 58, 103, 137, 178; context-menu-styles 67; inline-svg-styles 113; history-styles 301; JS matchMedia FB:12,
  context-menu-script.js:502, 1091.
- Phone layout: box full width (2044-2047); .filter-search sticky top 0 z 100 card bg (2050-2056); Filters line flex
  (2063-2068) + readout rides it (FB:265-275); bar static (2149); .nav-filters hidden (929); #searchbar 16px (2165). Title
  line no band rule.

## 7. Overlays and layers
- Lightbox report-lightbox-function.js:1-52 div.lightbox-overlay > img; kronikol_layers.lightbox (18-23); close via
  kronikol_nav.layer('lightbox', null, false) (24-28); open layer('lightbox', {at}, true, anchor) (30-34); Esc (35); CSS
  1155-1173 fixed z 10000 literal rgba .85 scrim; always included (RG:1509).
- Iflow popup internal-flow-popup-script.js: kronikol_layers.iflow (61-66); showPopup (68-99) saves opener, builds
  div.iflow-overlay > div.iflow-popup > button.iflow-popup-close[aria-label=Close][title="Close (Esc)"]; backdrop click + Esc
  (292-294); NO role=dialog/aria-modal/focus move/trap. CSS internal-flow-popup-styles.css:1-38: .iflow-overlay fixed inset 0
  bg var(--kron-overlay) z 20000 flex centre; .iflow-popup bg var(--kron-card) radius 8px shadow 0 8px 32px rgba(0,0,0,.25)
  max-width 90vw max-height 85vh overflow auto padding 20px; included with flow tracking only (RG:1654-1655).
- Context menu: close 419-421; outside click/Esc/scroll 1104-1110; toast 686; CSS context-menu-styles.css:1-114; phone bottom
  sheet (67-114).
- NOTHING modal: no <dialog>, showModal, aria-modal, role=search, type=search, aria-keyshortcuts anywhere. Tooltips = native
  title. Search help, tools gear (report-features-tools-script.js:200-206), scenario settings = popovers not layers.
- Layer API NAV: layers/openLayer/setLayer 360-372; layer(kind, data, open, opener) 378-398 (false before nav started; open
  saves opener + pushes {kind:'layer'}; close history.back() only when current entry is that layer's); arrive() 467-476
  closes/opens layers on travel, closes any open layer on "go" arrivals (following #sid- link from overlay closes it); reload
  clears layer 696-698; kronikol_nav 705; link capture a[href^="#scenario-"], a[href^="#sid-"] honours modifiers 617-628;
  address(el) needs id/stable id/sid/href to restore focus 92-108 (opener needs an id); capture() skips fixed/sticky
  149-180; status live region .kron-nav-status 400-413; describe() reads .filter-readout 434-437.
- Focus script leaves fixed elements (report-focus-script.js:16-24). BACK §4.7 :316-336 dialog close must take entry out.
  Wiki Generated-Reports.md:1230-1236.

## 8. Keyboard
- "/" KN:13-22 (if field hidden, "/" swallows key and does nothing; contenteditable not guarded). Arrows/Enter KN 25-48.
  report-tabs-script.js:55-73; report-features-tools-script.js:215-238; toggle-script.js:50-63; FB:121-123;
  report-search-function.js:59-60; report-focus-script.js:59-63. No Ctrl/Cmd+K or modifier-letter shortcut. Modifier
  handlers only diagram zoom (context-menu-script.js:1665-1689, skip fields 1672) + Ctrl+wheel (1693-1699).
- Esc: lightbox 35, iflow 292-294, context menu 1107-1109, diagram deselect 1146-1148, riding box FB:236-238; none stops
  propagation.
- "/" documented/tested: RG:1944; wiki Search-Syntax.md:7, Generated-Reports.md:708, 965; E2E ReportTabsTests.cs:72-84.

## 9. Specifications / labs
- Specifications RG:532 includeTestRunData false, violet sheet, Resolve(options, specifications: true); TestRunReport RG:537.
  Same title line + toggle (RG:1766), same box with rows-folded; no run band, no Summary view, no sort; bar only when
  durations (Features + Timeline tabs), else plain toolbar row, no Filters. Tests: ReportTabsTests.cs:167-173;
  FocusScriptTests.cs:73-83; ViewportSweepTests.cs:85-87; ThemedPagesTests.cs:107-129. Wiki Generated-Reports.md:676-677,
  685-687.
- Labs LabsReportGenerator.Build (35-80): theme scripts (46, 57), sheets (47-53), header (62-67); no box/bar/search (remarks
  16-21); labs-styles.css:4-17. Tests ThemedPagesHtmlTests.cs:74-149, ViewportSweepTests.cs:132-216.

## 10. Tests
- Helpers: OpenFilterRowsAsync (PlaywrightTestBase.cs:115-127) clicks `.filtering-box.rows-folded .more-filters` if
  visible; FillSearchBar (356-363) FillAsync + keyup (needs visible). ~58 FillSearchBar calls/24 files, 56
  OpenFilterRowsAsync/24 files, 47 direct #searchbar locators/14 files. BrowserRenderWorkerTests:169,174 +
  ZoomRobustnessTests:254 drive search from JS.
- Unit: FilterBarTests.cs 54-67 (phone toggle), 69-81 (rows-folded seed + More), 91-104 (exact NavFilters + title), 106-117
  (close), 127-137 (page without tabs has no bar group); RunBandTests.cs:119 StartsWith rows-folded tag; FilterRowsTests.cs:
  57-62 Box() slices from rows-folded tag (~dozen facts 76-245); StylesheetRulesTests.cs 210-216 fold rules (215 no desktop
  override), 218-234 phone toggle + box colours, 77-126 breakpoints; ControlTitlesTests.cs:337-395 (351 "Show or hide the
  filters"); ReportTabsTests.cs:74-86, 153-165; ReportThemeTests.cs:91-100; ThemedPagesHtmlTests.cs:107-126.
- E2E: FilterBarTests.cs 161-194 (hidden at rest 165, visible floating 168), 196-207 (hidden again 206), 77-159, 209-225,
  277-357; MobileFilterLineTests.cs all 9 facts (55-174); MobileResponsiveTests.cs:78-91, 109-123, 330-365, 369-378,
  540-567; ViewportSweepTests.cs:239-249, 300-319, 336-351; RunBandLayoutTests.cs:45-61, 108-137;
  ReportSectionWidthTests.cs:40-126; FocusClearOfTheBarTests.cs:426-464; ControlTitlesAuditTests.cs:346-354 (floats bar then
  clicks .nav-filters-toggle); ThemedPagesTests.cs:121-126, 151-198; ReportToolbarTests.cs:192-330; ComponentsTabTests.cs:191;
  TimelineTabTests.cs:213; FilterPipelineTests.cs:75-76, 367, 382; NotATestPaintTests.cs:162, 234, 247;
  BackForwardRestoreTests.cs:169, 195, 348, 393-398; BackForwardFilterTests.cs:19, 120; BackForwardPlacesTests.cs:283;
  FilterRowLabelTests.cs:17, 30-31; TagFilterTests.cs:69-259; ScenarioInteractionTests.cs:377-399;
  ToolbarStylingTests.cs:83-106, 300-301; UrlHashFilterHighlightTests.cs:101-202; StableIdDeepLinkTests.cs:296-310;
  TagRowVolumeTests.cs:65 (on demand); ShowcaseReportTests, WikiGifTests, WikiScreenshotTests skipped.

## 11. Exports
- export_html (report-export-function.js:119-146) head.innerHTML + <h1>Filtered Report</h1> + visible features + data
  scripts (47-84); compressed 136-144; export_csv 200-223; neither depends on box visibility/rows-folded (inline display
  only). Export buttons only in box header (RG:1925) -> hidden box hides them. Exported page has no box; FB start() returns
  early (289-290). New head scripts must no-op when markup missing. Wiki Generated-Reports.md:994-1002.

## Reuse list
- Tokens: scrim --kron-overlay; surfaces --kron-card, --kron-strip (header/footer strip), --kron-panel (results ground),
  --kron-field (input), --kron-hover (row hover); selected --kron-selected/-ink/-edge, --kron-accent-rail; edges
  --kron-hairline, --kron-hairline-soft, --kron-divider-soft, --kron-select-border, --kron-control-border; text --kron-ink,
  --kron-ink-soft, --kron-muted, --kron-label + --kron-fs-label + --kron-label-track (uppercase group heads), --kron-faint,
  --kron-on-accent; status --kron-pass/fail/skip/bypass (+soft), --kron-warn; --kron-code-bg; shadows --kron-card-shadow,
  --kron-shadow; focus --kron-accent; type --kron-font, --kron-mono, --kron-fs-*; --kron-nav-h.
- Classes: .theme-toggle look via shared selector; .theme-icon stroke style for magnifier; #searchbar (or move real
  #searchbar into overlay, keeping search_scenarios, deep chip in .search-field (report-search-index.js:472-483; CSS:296),
  "/", violet rule Stylesheets.cs:132, FillSearchBar tests); .search-help-panel/.search-help-table/.search-help-note kbd
  (CSS:333-397); counts .more-filters-count (284-289), .tab-n (800), .nav-readout.is-filtered b (865-868), .filter-readout
  (561-569); .nav-filters-toggle states (879-885); close .filters-close (893-898) / .iflow-popup-close; overlay geometry
  .iflow-overlay/.iflow-popup; result rows = failure-cluster rows (RG:2192-2193, CSS:1580-1601) with <a href="#sid-…">;
  empty .filters-empty (900-909); tabs .report-tab/.view-btn (792-818); .status-toggle segments (511-520); [hidden] (TOK:333);
  layer via kronikol_layers with an id on the opener.

## Assumption sites "box visible by default" / "Filters only while floating"
- CSS 194-204, 269-271, 290-293, 857-858, 887-892, 922-927, 928-929, 1981-2011, 2044-2068; Stylesheets.cs:124-127, 132.
- JS FB 1-9, 56-83, 94, 188-211 (title 199), 212-228 (title 223), 241-253, 260, 265-275, 296-301, 304-310, 330-338;
  report-search-function.js:3-9; KN:16-22; report-init-script.js:10-13 (phone rows folded from first paint);
  report-scenario-feature-map-helper.js:93-130.
- C# RG 1882-1883, 1912-1927, 1944, 2043-2045, 3062-3067, 3115-3120, 3122-3128, 3130-3132.
- Plans OPTION_C :90, :122, :412-451, :1089-1100; INV :89, 205-206, 304-313, 457-479, 830-832; stylesheet.md 64-67, 225-230,
  501-502; mock c-round8.js:173-238, c-round17.css:93-96; nav-check.js:99, 148; edits-s7-a.py:100-102; gen-s7.py:17, 67;
  s12p2/design.md:60-64; BACK harness inventory.md:128, 133.
- Wiki Generated-Reports.md 98-99, 682-685, 692-700, 707-709, 717-726, 965, 994-1002; Migrating-to-v5-Report.md:22, 68-70;
  Migrating-to-v5.md:17; Search-Syntax.md:7. CHANGELOG 1734-1738, 2000-2004.

## Gaps
1 Phone: no Filters outside box at <=768 (CSS:929, open() refuses, bar static -> scrolls away).
2 Pages without tabs: no bar, no toggle (RG:1685, 2045).
3 Screen readers: only live region inside box.
4 "/" does nothing while field hidden.
5 fit() doesn't rerun on k-floating change; an always-visible toggle makes it consistent.
6 Search button needs an id (focus return) and a class other than theme-toggle.
