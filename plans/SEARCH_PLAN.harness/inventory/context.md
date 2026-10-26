# Plan conventions, earlier decisions, documentation and release context (2026-10-26)
Repo main 29e551f3 (5.8.0); wiki 1f7944e (5.8.0, matches origin); K4J local main 82 behind origin -> cite ledger from origin/main.

## Plan conventions
- plans/README.md:3-14 (plans are records, not promises; PLANS_STATUS index). CLAUDE.md:50-52. Plan-only commit = no bump,
  style "Plan (no bump): ..." (47c250ec, cd993d45). Plan not pushed until owner says (BACK :714-715).
- PLANS_STATUS.md: header :1-16 (stale date line), table header :18-19, rows newest first from :20 (ZOOM 2026-10-23) to :121;
  archives :125/:155/:167; In progress :270, Not started :354, Cross-cutting :367.
- Row: | `NAME_PLAN.md` *(new, date)* | glyph **status** owner quote ... | shipped or — |. Not-green-lit form (row :98):
  | `TOOLBAR_REDESIGN_PLAN.md` *(2026-08-31; first indexed 2026-09-21)* | ❌ DRAFT, nothing implemented, **NOT green-lit**. ... | — |
- Structure (BACK/ZOOM/LONG): title "# NAME_PLAN: one-line claim"; bold status paragraph (status, dates, commit every file:line
  refers to, file abbreviations, harness pointer); §0 In short (ask, why fails today, cost, what changes, found on the way,
  version/releases, questions); §1 The ask (owner verbatim blockquote + "requirements follow, every decision held to them");
  §2 What happens today (file:line inventory tables); §3 Measured M1..Mn; §4 Design numbered rules (Z1..); bugs on the way
  (S#/B#/F#); Tests (fact tables with "Red on X because" column, core facts, "Pins that move", proofs red-first + mutation
  script, which browsers); Releases (order, worktree/branch, steps, docs: wiki lines, README, changelog, K4J ledger,
  acceptance on a real report); Questions Q1..Qn with (a)/(b) + recommendation ("Work proceeds on each recommendation unless
  the owner says otherwise"; "Recommended answers first"); Log (dated); Appendix A harness.
- Releases: R0 patch (bugs found) then R1 minor, R2 follow-up minor; owner can merge (ZOOM shipped R0-R2 as one minor 5.7.0:
  "Can you implement the plan in full as a minor release"). BACK argued one release not three (:631-648). Version numbers
  never assigned in advance (ROADMAP :19-21); claim "taking X now" at push; placeholders like 4.14.R1.
- Harness: mock/, checks/ (probes, mutate-*.py or mutate.js — "node, as this machine has no Python" ZOOM :507), inventory/,
  edits/, wiki/, kronikol4j-ledger.md; probes via tools/playwright/installed-browser.js.

## OPTION_C (5.0.0) decisions (in plans/OPTION_C_PLAN.harness/inventory/structure-and-behaviour.md = INV)
- D13 INV:286-290 exports drop "Filtered" narrow (span.wide-only). D15 INV:304-315 More filters, box starts .rows-folded,
  count while folded, help opens while folded (.help-open); risk "high: on desktop every row control out of view".
- D16 INV:317-339 qualifiers dep:/duration:/is:happy; owner R17.1 "dep: and duration: must not clash with real searches".
  D17 INV:341-347 deep-search chip inside the field. D18 INV:349-355 measured layout (.pair-fits/.trio-fits, --kron-nav-h).
- D24 INV:457-480 bar carries filters (riding, cap 60%, X/Esc/phone/rest puts back; div.nav-float-sentinel -> .k-floating;
  "Filters button shows only then"; desktop only; phone keeps box's own Filters toggle). D25 INV:482-485 .is-pinned.
- D49 INV:730-744 titles on every control (rule table INV:753-814): #searchbar :789, .search-help-toggle :790,
  .mobile-filter-toggle :800, .nav-filters-toggle :830, .filters-close :831, .more-filters :832.
- Target tree INV:89-102: div.filter-search > input#searchbar, button.search-help-toggle, button.more-filters; help panel
  inside div.filters#filter-rows.
- Why Filters only while floating: mock c-round8.js:226-227 "Round 17 (owner, item 11): the Filters button shows only while
  the bar floats; at rest the box is in view above the bar." Shipped stylesheets.css:923-925
  `.toolbar-row.report-nav:not(.k-floating) .nav-filters-toggle { display: none; }`; phone stylesheets.css:927-928
  `@media (max-width: 768px) { .nav-filters { display: none; } }`. Fold rules stylesheets.css:291-292 (every width).
- Title line S1/D1 INV:153-166 div.title-line > h1 + button.theme-toggle; theme is the ONLY thing stored
  (PersistentFilterStateReportTests.Report_stores_nothing_but_the_theme); Q1 :159-163 "allow it for the theme only, never
  for filters or toggles". Code ReportGenerator.cs:96 (button), :1766 (title line). Labs + Specifications same title line.
- Phone S12: one breakpoint 768px/768.02 (StylesheetRulesTests.The_phone_breakpoint_is_one_in_every_sheet_and_every_script);
  rows fold under phone Filters button on own line with readout; MobileFilterLineTests (8 facts); phone sticky search line
  pinned by MobileResponsiveTests.cs:367-377; design edits/s12p2/design.md:59-83.
- Owner notes (mock comments): round 8 c-round8.js:2-8 box in bar "is part of the bar, not a layer over the page... covers
  nothing... desktop only"; round 9 c-round9.js:2-5 "everything under the search field stays folded until you ask for it";
  round 16 c-round16.js:41-43 "every filter can be written in the search bar... as qualifiers, a key and a colon, as GitHub,
  Gmail and Slack searches write them"; ANY default. TOOLBAR_REDESIGN :29 dependencies search syntax.

## Deep search (SEARCH_INDEX_PLAN deleted; git show 82abeb7f:plans/SEARCH_INDEX_PLAN.md = SIP, 663 lines)
- SIP:97-119 index embedded <script id="kron-search-index"> gzip+base64, hashed trigram -> scenario bitset, exact verify,
  scenario granularity, shallow first then deep merged; FNV-1a 32 B=65536 KSI1 (SIP:225-244); @tag/$status never touch index;
  verify via existing matcher -> sr flag -> applyVisibility -> update_url_hash (SIP:264-283); Blob worker + LRU, no
  IndexedDB/localStorage (Q-B :606-616); window.__kronikolSearch telemetry; chip "+N more found in payloads & diagrams" /
  "results refined (+N/-M)"; "There is no result-count UI today anywhere (only #failure-counter)".
- Costs: M1 2,000 scenarios 147.1 MB raw -> 1.02 MB blob, decode 6 ms; hex/GUID candidates≈all; normalize+load 3.1 s/147MB.
  M2 DeepQueryMs 70 ms cold fixture, ~2-3.5 s cold on 2,000 docs. M3 +0.15-0.35 s generation (~5-10%). Linear scan 98 MB in
  memory ~30 ms (index = decompression/memory gatekeeper). Wiki: adds 0.25-1 MB.
- §10 verbatim (SIP:554-559): "Auto-open the matching diagram / flash the matching note when a deep-only match is opened. The
  verify pass already knows which source (payload vs message vs flame vs step) matched, so this is purely additive UI over the
  same index — explicitly not part of the core deliverable; build later or never." Not built: out of scope; per-row highlight
  "unreachable by construction" (SIP:22-25); ROADMAP:988 "Deferred by design"; wiki Search-Syntax.md:297-299 gap text
  ("open the scenario and use the browser's in-page find, or kronikol query grep").
- Since: 5.5.4 WebKit file:// fix (CHANGELOG:408-416); 5.6.0 index only gzip item; ROADMAP:1023 open index defects (drops
  markup-looking payload text, ASCII-only case folding) offered as patches, not green-lit.

## Roadmap / other plans
- No search redesign listed anywhere. ROADMAP "search" hits are SEO/defects/registers (:241 search help overflow fixed;
  :988 Phase 2; :1023 index defects). V6_PLAN only "Elasticsearch" :249; V6 :225-235 toggle-defaults unification relevant if
  a new ReportToggleDefaults property is added. No filter-box property today (ReportToggleDefaults.cs:18-136).
- TOOLBAR_REDESIGN §5 Q3 never answered: compiled-expression readout under filter rows (paste-able into search, teaches DSL)
  :338-339; §2.5 :215-226 dep:, dep:none, @"..."; §2.6 :228-233 URL hash v2; "Specifications is its own document: filtering
  box full-width at the very top" :68-75; verify against generated report never mocks :309-313.
- TOOLBAR_AT_EVERY_WIDTH: search input not a floor under box width (:87-88, :194, :308-312); "search help scrolls at every
  width", "Every sweep opens the search help and the timeline" (:1313-1321); overlays not swept (:1340-1341);
  tr.row-search-match (:1104-1105). STAGE_1 :32-37 320-1400 px sweep permanent guard.
- REPORT_FORMAT :866-869 search reads data-search + deep index; Q8 :1252-1260 sub-steps drawn on first open, Ctrl+F loses
  them, "Kronikol's own search finds it"; #step-data apart from #puml-data (:853-855). TAGS :498-501 @tag same set as Tags row.
- Keyboard: "/" focuses #searchbar except in a field (report-keyboard-navigation-function.js:13-22; 5.2.7 fix CHANGELOG:992).
- BACK model (:157-178): rule 1 place = entry; rule 2 layer = entry while open; rule 3 run of one control's changes (typing) =
  one entry; rule 4 More filters + search help = "adjusting", never an entry. Layers :316-336: "If ... a <dialog>, its close
  event must take the entry out." Context menu, search help, gears are not layers. Search waits 300 ms, writes at once on
  Enter (:348-351).
- Placement: ROADMAP:70-104 rules; §8 :701-704; edits per TAGS_PLAN.md:781-797 (stage row, D number, "Amended" sentence on
  line 3, §7 plan list, Appendix B.1 row), claim by message first. Next free D41; last stage-1 row 1.32 (:267); B.1 :818-870;
  §7 :674-697. Precedent since 5.0.0: feature plans took no row (LONG "no roadmap row"; HTML_COMPRESSION Q12 "owner's call;
  not placed").

## Semver precedents
- CLAUDE.md:20-22 MAJOR includes "changing a default so that existing code behaves differently"; :30-32 report output
  change not major on its own; new option is minor.
- 3.21.0 (CHANGELOG:11163-11167) and 4.6.0 (:7570-7576): sections left the default report as MINOR ("No existing option's
  default changes... What a default report draws changes, which is a report output change, recorded in the K4J ledger");
  HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md:285-301 warns re-enabling later "flips a default" = major.
- 5.0.0 "Changed (breaking)" (:1800, :2000-2005): "On a desktop the filter rows fold under More filters... a script or test
  that uses a row's control on a desktop opens them first" (done in the major).
- 5.4.0 (:822-826): default-on change shipped as minor at owner's word ("CLAUDE.md files a default that changes what an
  unchanged project's report draws under MAJOR; the owner chose to ship it in a minor all the same").
- Minor justifications: 5.1.0 new control; 5.2.0 new keys of a documented address; 5.5.0 new report feature; 5.7.0 new
  behaviour of a report control + new diagram feature.

## Kronikol4J
- Ledger: K4J docs/REMAINING_PARITY.md "## .NET-side features shipped after this audit (divergence ledger)" :1877; entries to
  :2516 on origin/main; "## Explicitly OUT OF SCOPE" :2518; new entries at end before OUT OF SCOPE. Port page pinned 3.0.43
  (K4J README :9-11, :18). Commit "Ledger: .NET X.Y.Z, ...". Analogues: 5.1.0 filters entry :2488, 5.2.0 Back/Forward :2490.
  Example entry format: "- **<what> (.NET 5.5.4, 2026-10-23, `plans/HTML_COMPRESSION_PLAN.md` F2).** Not mirrored: ... Byte
  parity breaks on every page ...; the data files are unchanged. What the page gains: ..."
- Port emits 3.0.43 filter box (DotNetHtmlReportRenderer.java:738-813; searchbar :740; help :743/:755; toolbar-row :818);
  no nav-filters-toggle/more-filters/rows-folded/report-search-index.

## Wiki pages to rewrite (C:\Code\Kronikol.wiki @1f7944e)
- Generated-Reports.md: :94-95 title line; :98-99 filter box; :644-650 theme switch; :673-688 report bar + Filters;
  :692-703 phone; :705-732 Search and filters ("/" + "?" + qualifiers + More filters + Clear All focus to search);
  :759-769 link q=; :963-990 keyboard and tooltips; :994-1002 exports ("on the filter box's Filtering line"; export has no
  run band, filter box, report bar); :1134 param row highlighting; :1193-1197 q= key; :1216-1245 Back/Forward.
- Search-Syntax.md: :1-7 tip ("/" and "?" icon); :157-213 qualifiers (amber border :205/:211; q= and Clear All :213);
  :247-310 deep search chip (:277-279) + gap (:297-299). Inbound anchors: Tags-and-Attributes.md:68,
  Marking-New-and-Changed-Tests.md:17, Cross-Run-History.md:647.
- Migrating-to-v5-Report.md :16, :22, :68-70 (tests press button.more-filters; span.search-field), :54-59, :85-86.
- Migrating-to-v5.md:17; Report-Themes.md:29-30; PlantUML-Browser-Rendering.md:169, :261; Large-Response-and-Diagram-
  Handling.md:36; "search bar" wording: Tags-and-Attributes.md:22,:68; Marking-New-and-Changed-Tests.md:3,:16;
  Integration-BDDfy-xUnit3.md:249; Integration-ReqNRoll-xUnit2.md:229, -xUnit3.md:226, -TUnit.md:224;
  Cross-Run-History.md:17,:630,:642,:901; FAQ.md:109; Home.md:63; Querying-Reports.md:1028; Step-Tracking.md:267;
  Report-Configuration.md:123,:174,:220; Migrating-to-v5.md:90; _Sidebar.md:108,:110. Link checker tools/wiki-links/wikilinks.py.

## CHANGELOG
- Last: 5.8.0 (:7), 5.7.3 (:40), 5.7.2 (:130), 5.7.1 (:152), 5.7.0 (:180). Format: "## [5.8.0] - 2026-10-25" + paragraph
  "The minor part moves (5.7.3 to 5.8.0): <why>." + ### Changed / ### Fixed / ### Tests with bold lead sentences.
- Search/filter-related since 5.0.0: 5.7.3 :79-80; 5.6.0 :360, :364-371, :386-387, :392; 5.5.4 :408-422; 5.4.3 :622-624,
  :668; 5.2.7 :992-1027 (+tests :1077, :1089); 5.2.0 :1444-1451, :1470-1483, :1524 (VerdictFencesTests pins search fn
  bytes), :1534-1537 (Kronikol.Tests.SearchEngine Jint); 5.1.0 :1602-1608, :1640-1643.

## Release checklist (for the plan)
1. TDD red->green; core suite + full Playwright suite pass; release.slnf builds Release; bundled browsers only; announce
   Playwright runs across sessions (BACK :612-616).
2. Worktree from origin/main, LF (`git -c core.autocrlf=false -c core.eol=lf worktree add`).
3. Bump every package: Directory.Build.props:4; .claude-plugin/plugin.json + marketplace.json;
   templates/github-actions/kronikol-history/VERSION; template pins (5.4.4 today, e.g.
   templates/kronikol-mstest/KronikolComponentTests.csproj:30) move only once nuget lists every id.
4. Changelog says which part moved and why; fold [Unreleased]; call out behaviour changes.
5. "taking X now" to live sessions before push. 6. Commit, tag v{version}, push both (no "fixes #N"; never quote CI skip
   marker). 7. Check CI, CodeQL, Summary Preview runs. 8. After publication: wiki (tools/wiki-links), K4J ledger line,
   PLANS_STATUS + ROADMAP updates, memory.
- Test exposure: PlaywrightTestBase.cs OpenFilterRowsAsync (:115-127), FillSearchBar (:356-363). Counts across tests/:
  FillSearchBar( 58 lines/24 files; OpenFilterRowsAsync( 56/24; searchbar 59/22; more-filters 16/9; nav-filters-toggle 3/3.
- CLAUDE.md :54-66 Playwright rules incl. "Search bar — use FillSearchBar() helper which dispatches keyup..." (changes if
  search moves). Screens: Chrome + Firefox, light + dark, 1400 px + phone (OPTION_C :152-153); WikiScreenshotTests,
  WikiGifTests (search GIF WikiGifTests.cs:680-711). Mutations: BACK :602-608, ZOOM :505-517.

## Conflicts the agent flagged
1. Filters-only-while-floating premise gone (stylesheets.css:923-925 + FilterBarTests fact).
2. More filters pointless; rows-folded model (S12 p2) and 56 OpenFilterRowsAsync calls affected.
3. Phones: bar static, .nav-filters hidden (stylesheets.css:928); box keeps own Filters toggle; sticky phone search line
   pinned (MobileResponsiveTests.cs:367-377).
4. Pages without a bar: Specifications.html without durations has no bar (wiki Generated-Reports.md:685-687); exported page
   has no filter box (:1002).
5. Overlay = BACK rule 2 layer; results view = rule 1 place needing an address key; round 8 "not a layer over the page"
   was about the box in the bar.
6. No browser storage: only theme stored -> recent searches need owner decision (OPTION_C Q1 :159-163; SIP Q-B).
7. Deep search finds scenarios not hit locations -> snippets need Phase 2.
8. Fixed contracts: "/", onkeyup hook, q=, kron-filter-change, VerdictFencesTests pins, K4J 3.0.43 search line.
