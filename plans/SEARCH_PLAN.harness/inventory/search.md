# The search today: a line map (2026-10-26, main 29e551f3, 5.8.0)
Plan of record deleted: `git show f3f7318d^:plans/SEARCH_INDEX_PLAN.md` (= SIP).

## 1. Markup (ReportGenerator.cs = RG)
- ONE emission site RG:1923-1947 in GenerateHtmlReportCore (:1396), outside `if (includeTestRunData)` (:1771-1887) => every
  page: Specifications (:532, includeTestRunData:false; empty file when any scenario failed :1450-1451; violet sheet
  ReportConfigurationOptions.cs:66), TestRunReport (:537), public GenerateHtmlReport (:1295-1387), merge
  (Merge/MergeableReportRenderer.cs:93). Labs page: no search (LabsReportGenerator.cs:15-21, :46, :57). Export has no box.
- Page order: .title-line h1 + theme switch (:1766); run band TestRunReport only (:1885); filter box; .nav-mark +
  .toolbar-row.report-nav (tabs + .nav-filters) (:2044-2058, :3127-3128); panels (:2101); Features panel opens with
  FiltersEmpty (:2103, :3135-3136).
- Box: header (:1925) readout span.filter-readout[role=status], Clear All, Export HTML, Export CSV, hidden .filters-close
  (:3131-3132); search line (:1926) input#searchbar (no type/role/aria-label), ? button (no aria-expanded), titles :3110/:3113;
  .mobile-filter-line (:1927); div.filters#filter-rows (:1928): help panel display:none (:1929-1945, 11 syntax rows
  :1931-1942, note :1944), Status (:1957-1966), Duration (:1990 data-p50/p90/p95/p99), Happy (:1994-1995), Tags (:2003-2014),
  Dependencies (:2017-2027); chip markup :3027-3028.
- data-search ONLY on: plain scenario details.scenario (:2459-2474, :2505): feature name, scenario name, feature description,
  endpoint, scenario description, rule, effective tags (no "Happy Path"), "retry N", error message (FAILED only :2467),
  background+step text incl sub-steps recursively (:7133-7141), diagram terms = participant names + `METHOD: url` only
  (:7104-7131), example values (:2817-2823); lowercased (ToLowerInvariant), HtmlEncoded. Parameterized group
  details.scenario.scenario-parameterized (:3532-3558, :3598) aggregated over rows (every row's error :3545; examples block
  name/desc :3550-3554). data-row-search on every tr (flat :3656-3675/:3685; grouped :3759-3778/:3793).
- Other attrs: data-status (group aggregate :3524-3530), data-tags (percent-encoded comma-joined + Happy Path,
  TagChips.cs:82-91), data-dependencies (:2428-2430; group union :3569-3570), data-duration-ms, data-warmup-ms (group total
  :3573-3574), data-runs, data-not-a-test, data-history-verdicts (ShowScenarioHistory :1478-1479), class happy-path, id,
  data-stable-id.
- Deep-only: full diagram CodeBehind (messages, payloads, headers, SQL) (:2601, :3963-3984); whole-test-flow activity
  PlantUML + flame text (:2647-2648, :2773-2811, :4004-4052); ErrorStackTrace index-only (:2530-2534, :3900-3901). Never
  indexed: component diagram, per-boundary iflow popups (SIP:146-147). Help row ":1932 diagram source" and wiki
  Search-Syntax.md:11 overstate instant search (only participants + URLs).

## 2. report-search-function.js (RSF)
- onkeyup -> search_scenarios() waits 300 ms (:36-48); empty runs at once (:40-42); Enter keydown (capture) + change flush
  (:51-61); NO input listener (mouse paste waits for blur); Enter keyup schedules a second identical run 300 ms later.
- run_search_scenarios (:168-337): fc() filter cache (built once, never invalidated; report-scenario-feature-map-helper.js
  = FMH :1-36, item fields :25); split_search_qualifiers (:142-166); q flag; kron-q-bad + data-q-bad (:177-178; CSS
  stylesheets.css:268; reset_filters clears report-export-function.js:7); advanced path if isAdvancedSearch (:183) per-item
  advancedSearchMatch(input, item.searchText, search_tags(el), item.status, item.verdicts) (:205, kron:verdicts fence
  :204-208); null = parse fail -> legacy (:209-212, :257); legacy path (:261-336) splitLegacyTagExpression +
  parseSearchTokensIncludingQuotes, every token includes (:281-301); empty -> sr=false, close_opened_by_search,
  applyVisibility, update_url_hash, _kronDeepReset (:267-276). After: applyVisibility(c), update_url_hash(),
  revealSingleMatch when exactly one (:221-226, :303-309). Row highlight: each tr[data-row-search] containing all text+phrase
  tokens gets row-search-match (incl negated in advanced), first matching row .click()ed (selectRow) (:228-253, :312-334);
  not undone on empty. window._kronDeepQuery(input) (:254, :336).
- applyVisibility FMH:64-90 single combiner: `var hidden = d.tag || d.dep || d.st || d.sr || d.dur || d.q;
  d.el.style.display = hidden ? 'none' : '';` (:68-69); rules/features hidden when no child shows (:71-88); rows never
  hidden; hold_visibility/release_visibility (:55-63).
- _openedBySearch (:18), revealSingleMatch (:19-29) also opens details.example-diagrams, .scenario-steps,
  .scenario-background unrecorded; close_opened_by_search (:31-34) only when text part empties.
- NO highlighting (<mark>/::highlight) anywhere; only .row-search-match (stylesheets.css:1813-1815; violet Stylesheets.cs:162).
- filter_readout (:93-130): "All N scenarios, M features" / "Filtered x of N scenarios, y of M features", is-filtered,
  data-none-shown; dispatches kron-filter-change {shown,total} (runs) only when signature changes (:125-129). Listeners:
  filter bar (report-filter-bar-script.js:329), navigation (report-navigation-script.js:680), timeline
  (report-timeline-script.js:115), components 150 ms (report-components-script.js:203-209).

## 3. Grammar (advanced-search.js = AS)
- isAdvancedSearch :9-11 (`/&&|\|\||!!|\$\w/` on lowercased text after qualifiers removed); advancedSearchTokenise :18-156;
  advancedSearchParse :169-250; advancedSearchEvaluate(ast, searchText, tags, status, verdicts) :265;
  advancedSearchMatch :323-336. Verdicts param fenced (:264-268, :322-326).
- Tokens: && || !! (:30-49), ( ) (:51-63), "phrase" (unclosed takes rest :65-83), @tag until ws/()/" and @"a tag"
  (:85-106), $status (:108-120), bare word (:122-136); `not` is plain text in advanced mode. Implicit AND (:139-153).
  Precedence NOT > AND > OR (:162-167); !! applies to one primary; leftovers -> null -> legacy (:240-249, :328-330).
- Matching: text/phrase searchText.indexOf (:272-275); tag exact in lowercased data-tags (:276-277); status equality with
  data-status lowercased (:284); other names -> verdict set (:285-287). $skipped does not match SkippedAfterFailure while
  Status buttons count SkippedAfterFailure as Failed (report-status-filter-function.js:22).
- Qualifiers RSF:63-166: SEARCH_QUALIFIER = /(^|[\s(])(!!\s*|not\s+)?(dep|dependency|duration|is):("[^"]*"|[^\s()"]+)/gi
  (:72). dep:/dependency: (:108-114) comma alternatives any, partial substring on qualifier_name (lowercase, Unicode
  letters/digits only :76) vs data-dependencies. duration: (:115-132) range a..b inclusive or op >= <= > < = (no op = >=;
  = within 0.5 ms); units ms s m min default s, decimals (:73, :92-97); p50/p90/p95/p99 from .duration-filters[data-pNN]
  (:144-145); own time = data-duration-ms - data-warmup-ms (:99-102); no duration fails. is:happy (+ happy-path, happypath,
  plurals) (:133-135) class happy-path. !!/not before a qualifier negates (:138). Placement (:80-91): in quotes = text; in a
  negated group = text + mark; else AND-narrow, dangling operator removed (:155-164). Unreadable = text + mark (:151).
- Legacy: @a and @b or not @c with parens (:339-463); and/or/not keywords only when query contains @ (:361-382);
  legacySearchWords (:343-355); phrases then words implicit AND (:386-405); both sides always evaluated (:421-440).

## 4. Deep search (report-search-index.js = RSI)
- Gen: one SearchIndexBuildCache shared by both reports (RG:526-528), prewarm (:2163-2168; SearchIndexBuildCache.cs:24-46);
  SearchNormalizer.Normalize (SearchNormalizer.cs:20-45) -> FNV-1a trigrams B=65536 (SearchIndexBuilder.cs:14-64); KSI1 v1
  bitset/delta-varint (:100-245), gzip+base64 (:248-256); `<script id="kron-search-index" type="application/json">"b64"`
  after #puml-data and #step-data (RG:2717-2720, :2740-2762); only when fullSearchIndex && scenarios (:2161); one doc per
  scenario incl outline rows, keyed by anchor id; compressed page keeps gzip (CompressedPage.cs:412-413).
- Client: pure section (:20-284, Jint-tested); lazy ensureInit (:586-629) on first query with a text term of >=3
  normalized chars (kronIsDeepEligible :215-232); worker = fns.map(toString) over a fixed roster of 22 functions +
  kronSearchWorkerMain(self), zstd decoder prepended on compressed page, Blob URL revoked at once (:548-567). Messages: init
  {index b64, #puml-data text, item metadata} (:619-628); worker answers anchors; page builds docToItem by walking up from
  getElementById(anchor) (many-to-one for outline rows) (:569-584), replies docmap; query {gen, input} (:683). Candidates
  over-approx over AST; tags/statuses/NOT never prune (:171-211). Verify: same matcher over normalized '\n'-joined corpus
  (:252-272, :369-391). Results: partial `matches` batches (item indices) every 100 (add-only) (:299, :417-418, :636-643);
  final done:true + stats {candidates, candidateItems, verified, ms} (:420-424); authoritative removal only when
  kronQueryCanRemove (advanced with !!) (:239-246, :649). Cancellation state.gen++ (:670, :687); worker checks latestGen
  before each candidate (:407, :420); page drops stale (:632). LRU corpora 64 MiB (:296-298, :360-374); 2 decoded zstd
  pieces (:335-349). onDeepResult writes c.items[j].sr + applyVisibility; never writes address, never revealSingleMatch,
  never highlights rows.
- Chip span.kron-deep-chip appended inside .filter-search .search-field (:472-483): "searching everything…" (pulse after
  200 ms), "+N more found in payloads & diagrams", "results refined (+N/−M)", "no additional matches" (:484-509).
- Telemetry window.__kronikolSearch {indexState unknown|absent|unsupported|loading|ready|error, docs, buckets, lastQuery
  {candidates, candidateItems, verified, added, removed, ms}} (:468, :603-607, :658-665).
- Worker returns item indices ONLY: no offsets, no piece kind (:416, :421-423).
- Verify corpus per item (:511-546, :376-388): data-search; #puml-data sources for .plantuml-browser[id] and
  .plantuml-inline-svg[id] w/o data-plantuml-z (:517-522); [data-plantuml-z]; .raw-plantuml pre (img mode); .failure-result
  pre; flame text .iflow-flame[data-flame-z] s[], f[i][1], m[i][1] (:276-284 mirrors RG:2797-2811); + tags, status, verdicts.
- Timings: tools/search-bench/README.md:19-33 (1.06 MB blob/98 MB, decode 8 ms, broad cold 324 ms Node), :35-46
  (DecompressionStream 0.16 ms/blob), :65-92 (M1), :94-103 (M2 DeepQueryMs 70 ms cold, ~1.1 ms/blob, 2-3.5 s/2,000 docs),
  :105-124 (M3 +0.15-0.35 s gen).
- Phase 2 claim "verify knows which source matched" does NOT hold now: verify joins pieces without boundaries, returns bool
  (:252-272, :388); normalization changes lengths (CRLF, break rejoin, creole/code-point decode, tag strip, \n-escape strip,
  space collapse :57-73) so offsets don't map back; piece->DOM link implicit (element id/attr order/pre). Shallow records no
  location either. Wiki Search-Syntax.md:297-302 says hits not pinpointed.

## 5. URL / state / nav
- Address `#[scenario-…|sid-…][&tab=][&view=]&<filters>` anchor first (report-navigation-script.js = NAV :9-13, :49-61,
  :77-84). url_hash_filters (report-url-hash-function.js = UH :139-163) order: q= encodeURIComponent(#searchbar.value)
  incl qualifiers (:141-142), status=, deps=, depx=, depmode= (non-default), hp=1/hpx=1, tags=, tagx= (each encoded),
  tagmode=, dur=, pctl=. update_url_hash (:168-170) -> kronikol_nav.wrote. apply_url_hash (:175-272): split & and =; holds
  visibility; reset_filters (clears field, sr, q, _kronDeepReset); `if (params.q) { sb.value = params.q;
  run_search_scenarios(); }` (:201-206); status, modes, chips (reads 4.x cats/catmode/__uncategorized__/(no interactions)),
  pctl, dur; release; reveal anchor. parse_url_hash gone since 5.2.0 (UrlHashFilterTests.cs:111).
- Persistence stubs (report-persistent-filter-function.js:1-2; RG:1546-1548); localStorage only kronikol.theme
  (report-theme-boot-script.js:6, report-theme-switch-script.js:11, :26). history.state.kronikol = {v:1, id, kind, key,
  scroll, focus, layer} (NAV:258, :299).
- kronikol_nav = {start, go, wrote, layer, address, find, hash} (:705). wrote(filters) (:597-610) ignored while applying
  (:484-487); changed control = keywise diff (controls/changed :575-596); search key filter:q; first write of a run pushes,
  later replace (replaceSoon); budget 120 ms gap, 80 writes/10 s (:249-290).
- Layers: window.kronikol_layers.lightbox (report-lightbox-function.js:18-23), .iflow (internal-flow-popup-script.js:61-66),
  each {isOpen, open(data), close()}, via kronikol_nav.layer(kind, data, open, opener) (:378-398); open pushes entry at same
  address; close via own button/Esc calls history.back().
- Back/Forward: popstate -> arrive({how:'travel'}) (:644-656, :459-531); layers closed/reopened (:467-476);
  apply_url_hash(filters) when differ (:482-488); scroll+focus restored; status line "Filters changed: <readout>"
  (:427-439); <html data-kron-nav> incremented per arrival (:443-444); Back doesn't close what single-match search opened.

## 6. Keyboard
- "/" (report-keyboard-navigation-function.js = KN :17-21) focuses #searchbar unless target INPUT/TEXTAREA/SELECT (:15),
  from a tab too; no layer check. ArrowDown/Up (:26-41) move between visible details.scenario (.scenario-focused, focus
  summary, open feature), skipped in [role=tablist] and .note-scroll. Enter toggles picked scenario only when target body/html
  (:42-47). Enter in #searchbar flushes (RSF:59 capture). Tabs: arrows/Home/End (report-tabs-script.js:55-68); ArrowDown/Up
  outside field/tablist/.note-scroll switches to Features + Full (:71-72); view switch same (report-features-tools-script.js
  :215-237). Diagram view tabs arrows/Home/End (toggle-script.js:50-62; only with iflow tracking RG:1657). Ctrl/Cmd +/-
  zoom selected diagram, skips fields (context-menu-script.js:1665-1689). Tab focus-clear watcher (report-focus-script.js
  :59-90; leaves position:fixed alone :18-23). Enter/Space on iflow links (plantuml-browser-render-script.js:2005-2009,
  :2031-2035).
- Escape: 5 independent listeners, none stops propagation: lightbox (report-lightbox-function.js:35), popup
  (internal-flow-popup-script.js:292-294), context menu (context-menu-script.js:1107-1109), diagram deselect (:1146-1148),
  riding box home + focus Filters toggle (report-filter-bar-script.js:236-238; fires even in #searchbar). Search field has no
  Escape handling.
- Passive keydown: NAV:332 records scroll; any keydown stops re-aim (:188-201); filter bar forgets scroll memory
  (report-filter-bar-script.js:121-123).
- FREE KEYS: "s" not used (only "/", arrows, Enter, Home/End in tablists, Ctrl +/-, Escape, Tab).

## 7. Tabs that follow the search
- Features Full: display:none; .filters-empty when is-filtered && data-none-shown (report-filter-bar-script.js:282-286,
  :312, :329); its Clear All clears + focuses #searchbar (:296-301).
- Timeline "Show: Filtered" default (RG:2906-2911); rows map data-sid -> [data-stable-id] -> details.scenario
  (report-timeline-script.js:44-52); .filtered-note "Filtered: this timeline shows the N of M scenarios that match the
  filters." (:92-99); markup RG:2937-2939 emitted :2242.
- Components: only where the report knows which scenarios made each call (:2302-2305, :2958-2961); redraw 150 ms
  (report-components-script.js:203-209); own note (:105-115); hides drawing when nothing left (:162).
- .filtered-note-clear delegated: clear all + focus search (report-filter-bar-script.js:304-310).
- NOT following: Summary view table (row note "hidden by the filters", report-features-tools-script.js:135-151), History,
  Diagnostics, run band, Next Failure (report-jump-to-failure-function.js:2); #sid- links show filtered_out_note "This
  scenario is hidden by the filters." (UH:49-77, :93-94).

## 8. Exports and #searchbar readers
- export_html copies whole <head> (every script) + visible features; prunes #puml-data/#step-data; copies other body-level
  scripts whole incl kron-search-index; no filter box (report-export-function.js:47-84, :119-146). export_csv visible
  items, skips data-not-a-test (:200-223).
- #searchbar readers/writers: url_hash_filters (UH:141-142); search_scenarios (RSF:41-42); run_search_scenarios (:170-171,
  NO existence check); reset_filters (report-export-function.js:6-7); "/" (KN:19-20); filter bar open() focus
  (report-filter-bar-script.js:209-210), empty-state + filtered-note focus (:299-300, :308-309); apply_url_hash guarded
  (UH:203-204); chip parent lookup (RSI:475).
- Clear All from: box button (RG:1925), .nav-clear (filter-bar :155), .filters-empty-clear (:296-297), .filtered-note-clear
  (:304-307), .filtered-out-clear (UH:61-64).

## 9. Tests
- Jint tests/Kronikol.Tests.SearchEngine (~179): JintTestBase loads raw advanced-search.js (:17-34): TokeniserTests 42,
  ParserTests 30, EvaluatorTests 25, EdgeCaseTests 22, IntegrationTests 19, BackwardCompatibilityTests 5, VerdictSearchTests
  6; VerdictDeepSearchTests 2 (VerdictSearchTests.cs:73-119); DefaultVariantVerdictSearchTests 2;
  SearchQualifierPlacementTests 8 (document stub :20-23); SearchIndexJintTests 18 (three scripts vs shared vectors :18-21,
  :35-39).
- Unit tests/Kronikol.Tests/Reports: SearchFunctionTests 37 (AngleSharp + hand-written C# port SearchFunction.cs still using
  search-hidden class :14, stale vs shipped JS); SearchDataAttributeTests 22; SearchIndexReportTests 18 (DistinctTextCount==3
  :234-264; byte stability :266-272); SearchIndexVectorTests 5; SearchNormalizerEquivalenceTests 2;
  AdvancedSearchReportTests 4; TagExpressionSearchReportTests 5; UrlHashFilterTests 6 (pins function bodies :41-121);
  UrlAnchoredFilterReportTests 4; ClearAllFiltersReportTests 5 (sb.value='' :51-56); PersistentFilterStateReportTests 3
  (:37-49); KeyboardNavigationReportTests 5; VerdictFencesTests 6.
- Pins on input/placeholder/titles/help: FilterBarTests (unit) :69-81 exact order filter-search > search-field > #searchbar
  > ? > More filters > #filter-rows; also :54-67, :106-125, bar markup :91-104; FilterRowsTests :217-226 placeholder + help
  rows + note; ControlTitlesTests :347-350; FormStateRestorationTests :87-99 autocomplete=off; StylesheetRulesTests
  :210-216, :218-228, :434; ThemedPagesHtmlTests :228-246; ReportThemeTests :91-100 exact title-line h1 then theme toggle.
- E2E: Search collection: AdvancedSearchTests 15 (help :165-209), DeepSearchTests 24 (cross-engine :203-219; telemetry
  :570-592; chip + #puml-data invariants :443-566), BackForwardFilterTests 10, HistoryFilterTests 3,
  UrlHashFilterHighlightTests 10 (q= restore :107-117), FilterPerformanceTests 4 (hand-built page :11-60). Elsewhere:
  FilterPipelineTests 17 (qualifiers + q link :330-355; event :250-271); FilterBarTests 15 (help :121-129; riding focuses
  search :162-196; empty state :210-260; chip in .search-field :360-367); ReportToolbarTests 6 search facts (:191-330);
  BackForwardRestoreTests R14 :451-461; ReportTabsTests Slash :74-84; FocusClearOfTheBarTests :425-466;
  MobileResponsiveTests sticky search line :369-379; ZoomRobustnessTests :244-262; ControlTitlesAuditTests (:14-21);
  ShowcaseReportTests + WikiGifTests script the search.
- Helpers: FillSearchBar (PlaywrightTestBase.cs:356-363, 24 files); OpenFilterRowsAsync (:120-127); SearchAndWaitForCount
  (AdvancedSearchTests.cs:16-22); SearchAndWaitForVisible + WaitForChipText (DeepSearchTests.cs:136-149);
  SearchAndWaitFor (HistoryFilterTests.cs:25); SearchAsync (CompressedReportTests.cs:97); HashSoonAsync + data-kron-nav wait
  (BackForwardTestBase.cs:43-48, :144).
- Perf: DeepQueryMs < 2000 x stretch (BrowserRenderWorkerTests.cs:166-174, :282; ContentionScale.cs:32-39).
- CI lane "E2E (Search & Filters)" (.github/workflows/ci.yml:105-107); Remainder filter-exclude (:140) kept in sync.

## 10. Wiki (working tree)
- Search-Syntax.md: tip :7; text :9-37; operators :39-79; @tag/legacy :81-111; $status/verdicts :113-155; qualifiers
  :157-213; examples :215-245; instant vs deep :247-310; limits :312-328; fallback :330-332.
- Generated-Reports.md: box :98-99, :717-732; search/qualifiers :705-716; link grammar :759-769, :1186-1214; keyboard
  :963-971; exports :994-1003; row highlight :1134; Back/Forward :1216-1245; kron-filter-change :720-721; index gzip
  :176-177; block search :1275.
- Report-Configuration.md: FullSearchIndex :21, :123; single-match :282; URL grammar :285-293 (still names parse_url_hash);
  deep vs hidden :294-297. Others: Querying-Reports.md:1028-1034; Tags-and-Attributes.md:22, :65, :68;
  Migrating-to-v5-Report.md:22, :68-70; Migrating-to-v5-Tags.md:14-16, :60; Migrating-to-v5.md:17, :90;
  Large-Response-and-Diagram-Handling.md:20; Cross-Run-History.md:17, :630-647, :901; Marking-New-and-Changed-Tests.md:3,
  :16-17; FAQ.md:109; _Sidebar.md:110.

## 11. Sizes/pins
- Lines/bytes: report-search-function.js 462/21,301; advanced-search.js 336/11,324; report-search-index.js 691/33,440;
  report-url-hash-function.js 272/16,286; report-navigation-script.js 706/38,998; report-keyboard-navigation-function.js
  49/2,827; report-filter-bar-script.js 342/20,134; report-filter-chips-script.js 184/9,500; FMH 130/7,239;
  report-export-function.js 222/13,531; features-tools 251; timeline 121; components 222; tabs 91; SearchIndexBuilder.cs 257;
  SearchNormalizer.cs 200; normalize.js 64; vectors 199,412 bytes.
- Pins: VerdictFencesTests SHA-256 of both variants of advanced-search.js, FMH, report-search-function.js,
  report-search-index.js, exactly 4 fenced scripts (VerdictFencesTests.cs:37-55, :70-89); CompressedPageTests SHA of decoded
  index (:305-319); SearchIndexReportTests DistinctTextCount + byte stability; shared vectors; worker roster
  (NoteCopyFidelityScriptTests.cs:74-89, VerdictSearchTests.cs:106-119); string pins AdvancedSearchReportTests,
  TagExpressionSearchReportTests, UrlHashFilterTests.

## Load-bearing facts (agent's list)
1 keyup-driven #searchbar (FillSearchBar; perf bench sets value + run_search_scenarios(); no existence check).
2 URL grammar (q= encodeURIComponent incl qualifiers; anchor first; tab=/view= non-default only; 4.x keys read; writes only
  via update_url_hash -> kronikol_nav.wrote; one control run = one entry; nothing pushed w/o user action; data-kron-nav).
3 One combiner (tag dep st sr dur q) inline display:none; kron-filter-change {shown,total} 4 listeners; readout text pinned.
4 Qualifiers stripped before matching, q flag, AND-narrow only, kron-q-bad/data-q-bad, text in quotes and negated groups.
5 Purity: advanced-search.js DOM-free; report-search-function.js loads without document; 179 Jint facts run shipped files.
6 Normalization x3 (normalize.js, SearchNormalizer.cs, RSI), KSI1 v1, B=65536, FNV byte-pinned to vectors.
7 Worker reads #puml-data never data-plantuml; fixed toString roster.
8 Deep add-only except !!; docs = scenarios + outline rows by anchor id; chip in .search-field, wording pinned;
  __kronikolSearch fields pinned.
9 VerdictFences SHA pins; balanced // kron: fences.
10 Markup pins: order filter-search>search-field>#searchbar>?>More filters; exact title-line (h1 then theme toggle); exact
  nav-row/nav-filters markup; titles + names on every visible control (D49).
11 No storage other than kronikol.theme.
12 Uncoordinated document-level keys ("/" ignores fields only; 5 Escape listeners; arrows; layers via kronikol_layers).
13 _openedBySearch closes only when text empties; row highlight shallow only.
14 Specifications shares all; Export Filtered HTML carries every script, no box.
15 LoadResource resolves by EndsWith (RG:76-85); every report-*.js embedded (Kronikol.csproj:96): new script name must not
  end with an existing script's name.
16 New E2E classes go in both Search & Filters filter and Remainder exclude list.
