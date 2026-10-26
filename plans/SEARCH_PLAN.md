# SEARCH_PLAN: a dedicated search modelled on GitHub's, and the filters behind the bar's Filters button

**Status: DRAFT, nothing implemented, not green-lit.** Written 2026-10-26 by kronikol-41 at the owner's ask (section 1).
Every `file:line` is at `main` `29e551f3` (5.8.0) under `src/Kronikol/Reports/` unless it says otherwise; wiki lines are
at `Kronikol.wiki` `1f7944e`; 5.9.0 (`37428293`, released the same day) changes none of the code files cited. Short
names: RG `ReportGenerator.cs`, CSS `stylesheets.css`, TOK `report-tokens.css`, FB `report-filter-bar-script.js`, RSF
`report-search-function.js`, AS `advanced-search.js`, RSI `report-search-index.js`, UH `report-url-hash-function.js`, NAV
`report-navigation-script.js`, KN `report-keyboard-navigation-function.js`, TABS `report-tabs-script.js`, FMH
`report-scenario-feature-map-helper.js`, EXP `report-export-function.js`. The harness is
`SEARCH_PLAN.harness/` (Appendix A): GitHub's pages measured live and from archived copies (`github-ref/`, section 3),
the current report measured on the live BreakfastProvider demo (`current/`, cited M1), the research notes behind
sections 2 and 4 (`inventory/`), and the probe scripts.
A sibling plan was being written the same day: `REPORT_UI_FIXES_PLAN.md` (kronikol-cf), whose I1 changes how a tag or
dependency chip cycles on a click (section 7.4).

## 0. In short

- **The ask.** A search of its own, built the way GitHub's is and laid out like it, in the report's own colours and
  spacing; its button on the title line just left of the theme switch; the filter box hidden until the bar's Filters
  button opens it, that button always shown, and every filter row open whenever the box is; one minor release.
- **What the page does today.** The search is a field on the filter box's second line (RG:1926). It narrows the report
  300 ms after each key (RSF:36-48), matching names, steps, tags and call targets at once and payloads, diagram text and
  stack traces in a worker (deep search, 3.0.70). It says which scenarios matched but never where (RSI:416, the unbuilt
  Phase 2 of the index plan). The box sits on a row of its own above the bar, its rows folded under More filters (5.0.0
  D15, RG:1924); the bar's Filters button shows only while the bar floats (CSS:922-927); on a phone, and on a page whose
  only section is its features, there is no Filters button outside the box at all (CSS:929, RG:1685).
- **What changes, in six parts (section 4).**
  1. **A search button on the title line**, between the title and the theme switch: GitHub's input-shaped button with a
     magnifier and "Type / to search", the switch's height, edge and corner; a magnifier alone on a phone. Once a search
     is applied it shows the query, with a clear button (4.2).
  2. **A query builder**, GitHub's: a dialog over the top of the page, opened by the button or `/`. The query with its
     qualifiers coloured; suggestions as you type (the search itself, a scenario or feature to jump to, a qualifier to
     complete, a qualifier's values with how many scenarios carry each); "Search syntax tips" at its foot. Enter runs
     the search (4.3).
  3. **A Search tab** that lays the results out as GitHub's results page does: on the left "Filter by" (Scenarios,
     Features, Steps, Diagrams, Errors, each with its count), then Status, Tags and Dependencies to narrow by, then
     Advanced, which writes a qualifier into the query; on the right the count and the time, Sort by, and a card per
     result with the text around each match marked, a page at a time (4.5).
  4. **Where it matched.** Deep search learns which piece of a scenario matched and returns the line around it, for the
     page of results on screen only; a result's snippet opens the scenario at that step, error or diagram (4.6).
  5. **The filters**: the box is hidden until Filters opens it; Filters shows at every width and every scroll position, on
     every page that has a filter box, and counts the filters on; the rows are always open (More filters, its fold and the
     phone's own Filters button go); the box opens at the top of the bar, where 5.0.0's box already rides (4.7).
  6. **Nothing that works today stops**: every query written today means what it meant, and narrows the Features list,
     the timeline, the components and both exports as it does now (Q1); a `q=` link opens what it opened; the index, its
     vectors and the theme-only storage rule are untouched (4.9, 4.10).
- **Found on the way (section 5).** Nine defects in the search and the keys around it: Enter runs every search twice, a
  pasted query waits for the field to lose focus, the help claims instant search reads diagram source, `$skipped` and
  the Status buttons disagree on a scenario skipped after a failure, a search's row highlight outlives the search, and
  more. Each is fixed by the release, with a fact that fails on 5.8.0.
- **Release (section 7).** One minor, as the owner asked, numbered when it is pushed: new controls, a new tab, new keys
  in the address and new qualifiers are new public surface; no option's default changes. Built in eight stages on one
  branch (S0 to S7); S0 is a prototype on real reports for the owner to look at before anything is built.
- **Questions (section 8).** Q1 to Q14, the recommended answer first in each; work proceeds on each recommendation unless
  the owner says otherwise. Q1 is the one that shapes the rest: does the query still narrow the whole report (recommended,
  as today), or only the Search tab.

## 1. The ask

The owner, 2026-10-26:

> Kronikol should have a dedicated search feature, and it should be based on GitHub search's design (ie the UX etc of
> GitHub's search functionality), and it should be laid out in a similar way, and generally copy all it's design
> decisions within reason (obviously still keep the colours/paddings etc matching the existing design system). Filter
> section should move to being hidden by default, and the nav-filters-toggle should always be shown and be the primary
> way to show hide the filters. The filters, when toggled on, should be fully expanded by default. This should all be a
> minor release. The search button should to the left of the dark mode toggle, similar to how github does it.

Eight requirements follow, and every decision below is held to them:

| # | Requirement | Where |
|---|---|---|
| A1 | A search of its own, with GitHub's UX and layout: the button, the query builder, the results page, and their design decisions, copied within reason | 3.5 maps each of GitHub's decisions to copy, adapt or leave, with the reason |
| A2 | The report's own design system: its tokens for colour, its type sizes, radii, control heights and spacing | 4.2 to 4.7 name the token or the fixed value for every part |
| A3 | The search button just left of the dark-mode switch, as GitHub's search sits left of its header buttons | 4.2 |
| A4 | The filter box hidden by default | 4.7 |
| A5 | The bar's Filters button (`.nav-filters-toggle`) always shown, and the way the box is shown and hidden | 4.7 |
| A6 | Every filter row open whenever the box shows | 4.7 |
| A7 | One minor release | 7.1 |
| A8 | Implied: nothing that works today stops working (links, exports, Back and Forward, the keys, the Specifications page, the compressed page, the Azure DevOps viewer, the 320 to 1400 px sweep) | 4.9, 4.10, 6 |

## 2. What happens today

### 2.1 The top of the page, measured (M1)

On the live BreakfastProvider report (Kronikol 5.4.1, 214 scenarios, 73 features; nothing on the title line, the box or
the bar has changed since), bundled Chromium, `current/probe.js`, light and dark identical:

| Window | Title line | Theme switch | Run band | Filter box | Search field | More filters | Bar | Filters (bar) | Filters (phone) |
|---|---|---|---|---|---|---|---|---|---|
| 1440 | y18, 1404 x 34 | 34 x 34 at x1388 | y66, h90 | y170, h97, folded | 1229 x 35 | 110 x 35 | y281, h132 | hidden at rest | none |
| 1280 | y18, 1244 x 34 | 34 x 34 at x1228 | y66, h90 | y170, h97, folded | 1069 x 35 | 110 x 35 | y281, h132 | hidden at rest; 76 x 28 once pinned | none |
| 1012 | y18, 976 x 34 | 34 x 34 at x960 | y66, h173 | y253, h97, folded | 801 x 35 | 110 x 35 | y364, h166 | hidden at rest | none |
| 768 | y18, 732 x 34 | 34 x 34 at x716 | y66, h173 | y253, h158 | 672 x 39 | hidden | y425, h93 | hidden (phone) | 56 x 26, in the box |
| 390 | y18, 358 x 68 (two lines) | 34 x 34 at x340 | y100, h273 | y388, h164 | 298 x 39 | hidden | y566, h137 | hidden (phone) | 56 x 26, in the box |

The title line is `div.title-line > h1 + button.theme-toggle`, a flex row with `justify-content: space-between` and a 12 px
gap (RG:1766, CSS:18). The switch is 34 x 34, 1 px `--kron-hairline` edge, 8 px corners, `--kron-card` ground,
`--kron-card-shadow`, an 18 px stroked icon (CSS:22-29). At 1280 the title is 450 px wide, leaving 760 px of the line
empty; at 390 the title takes two lines and the switch is centred against them.

### 2.2 The search

- **One field, one emission site** (RG:1923-1947), written on every page `GenerateHtmlReportCore` makes: TestRunReport,
  Specifications, the public `GenerateHtmlReport` and the merged report (`Merge/MergeableReportRenderer.cs:93`). The labs
  page has none (`LabsReportGenerator.cs:15-21`). `input#searchbar` has no `type`, role or label, only a placeholder
  ("Search... (@tag, $status, dep:, duration:, &&, ||, !!)") and a title; `?` beside it shows an 11-row syntax table that
  lives inside the folded rows (RG:1929-1945).
- **What it reads.** Only two kinds of element carry `data-search`: a scenario and a parameterized group (RG:2459-2474,
  3532-3558); each outline row carries `data-row-search` (RG:3656-3675). A scenario's text is its feature's name and
  description, its own name, description, endpoint and rule, its tags, `retry N`, its error message when it failed, its
  steps with their sub-steps, its example values, and from its diagrams only the participants and the `METHOD: url`
  targets (RG:7104-7141). Deep search adds what only the index holds: whole diagram sources (messages, payloads,
  headers, SQL), the whole-test-flow activity and flame text, and stack traces (RG:2530-2534, 2601, 2647, 3963-4052).
- **How it runs.** `onkeyup` waits 300 ms; Enter and `change` run it at once (RSF:36-61). Qualifiers (`dep:`,
  `duration:`, `is:happy`) are split off and narrow by AND (RSF:63-166); the rest goes to the advanced grammar (`&&`,
  `||`, `!!`, parentheses, `"phrases"`, `@tag`, `$status`; AS:9-336) or, failing that, to the legacy tag-expression
  path (AS:339-463). Each match sets a scenario's `sr` flag; `applyVisibility` hides with inline `display:none` whatever
  any of six flags hides (FMH:64-90); `filter_readout` writes "All 214 scenarios, 73 features" or "Filtered 12 of 214
  scenarios, ..." into the box's `role=status` readout and fires `kron-filter-change` (FMH:93-130), which the bar, the
  timeline, the components and the navigation script listen for. A single match opens its feature, rule and scenario
  (RSF:19-29). The deep worker answers with scenario indices only, never a place inside one (RSI:416-424).
- **The address.** `q=` holds the field's text, qualifiers and all (UH:141-142); Back and Forward put it back and run it
  (UH:201-206, NAV:482-488); a run of typing is one history entry (BACK_BUTTON_NAVIGATION_PLAN rule 3).
- **The keys.** `/` focuses the field from anywhere but a field (KN:13-21); nothing in the report opens a dialog, and
  five Escape listeners act independently (lightbox, flow popup, context menu, diagram selection, the box in the bar).
- **What follows it.** The Features list, the timeline ("Show: Filtered", RG:2906-2911), the components and both exports
  (EXP:119-146, 200-223); Clear All empties it (EXP:6-7).

### 2.3 The filter box and the bar

- **The box** (RG:1924-2030) is a card on its own row between the run band and the bar: a header (Filtering, the
  readout, Clear All, Export Filtered HTML and CSV, and a close button that shows only while the box rides in the bar),
  the search line, the phone's Filters line, and `#filter-rows`: Status, Duration and Happy paths on one line, then
  Tags and Dependencies, each a row of tri-state chips with ANY/ALL. It starts `rows-folded`, which hides
  `#filter-rows` at every width (CSS:290-291); More filters, at the end of the search line, unfolds them and, while
  they are folded, counts the filters on in them (FB:56-83). No option sets any of this (`ReportToggleDefaults.cs:15-137`
  has nothing for the box; only the ANY/ALL modes are configurable, RG:1489-1502).
- **The bar** (RG:2044-2058) is written only where the page has tabs (`useTabs`, RG:1685): it sticks to the top, and
  `.nav-filters` at the right of its tab row holds a copy of the readout, Clear All while anything is filtered, and
  Filters. Filters is `display:none` until the bar floats (CSS:922-927; the owner's item 11 of the 5.0.0 preview's round
  17, as the preview's script records it: "at rest the box is in view above the bar", `OPTION_C_PLAN.harness/mock/
  c-round8.js:226-227`); pressed, it moves the box into the top of the bar, where it pins with the
  bar, takes at most 60% of the window and scrolls inside; Escape, its close button, a phone's width or the bar coming to
  rest puts it back (FB:88-263).
- **On a phone** (one breakpoint, 768 px) the bar is static and `.nav-filters` is hidden (CSS:929, 2149); the box keeps
  a Filters button of its own on a line the readout rides (RG:1927, FB:265-275), and the search line sticks to the top
  (CSS:2050-2056).
- **The only live region** for the filters is the box's readout; the bar's copy is `aria-hidden` (RG:3128).

### 2.4 What pins it

About 58 calls of `FillSearchBar` in 24 test files, 56 of `OpenFilterRowsAsync` in 24, 47 `#searchbar` locators in 14;
unit facts that pin the box's markup order, `rows-folded`, the bar's exact markup, the title line's exact markup and
the search help (FilterBarTests, FilterRowsTests, RunBandTests, ReportThemeTests, ThemedPagesHtmlTests,
StylesheetRulesTests, ControlTitlesTests); about 179 Jint facts that run the shipped grammar and index scripts
(`tests/Kronikol.Tests.SearchEngine`); the SHA-256 pins of the four fenced search scripts (VerdictFencesTests.cs:37-89);
the shared normalization vectors (`tests/shared-vectors/search-index-vectors.json`); the worker's function roster
(NoteCopyFidelityScriptTests.cs:74-89); and the CI lane "E2E (Search & Filters)" with the Remainder lane's exclude list
kept in step (`.github/workflows/ci.yml:105-107`, 140). Section 6.3 lists every pin that moves.

## 3. GitHub's search, measured

Measured on 2026-10-26 with the bundled Chromium 149 (Playwright 1.61), headless, logged out, about 20 page loads
(`github-ref/probe4` to `probe10`); sizes and colours read from GitHub's production stylesheets of that day, and
behaviour from its search bundle (both named in `github-ref/README.md`; the probes fetch them again). One finding shapes the method: **logged out,
github.com no longer serves the query builder.** Its header now has a magnifier that opens a small "Quick search" dialog
with no qualifier suggestions. The query builder a signed-in reader uses (the Catalyst `<qbsearch-input>` with
`<query-builder>`) was measured from Wayback Machine copies of GitHub's own server HTML (2025-05-28, 2026-03-01,
2025-06-28), rendered with every live GitHub host blocked; those numbers are marked *archived*. Signed-in behaviour
that neither build shows (recent searches, saved searches) is taken from GitHub's documentation and engineering posts,
cited in `github-ref/README.md`. The full numbers are in `github-ref/measurements.json`; the probes write screenshots named by what
they show and the width (not committed: 106 PNGs, 7.5 MB, against 1.4 MB for all of `plans/`; the probes write them again).

### 3.1 The entry point

- **Where it sits.** In the signed-in header (October 2023 redesign) the search box is the first thing at the right:
  "Type [/] to search", then (since 2025) the Copilot buttons, a divider, Create, Issues, Pull requests, Inbox and the
  avatar. Search is the leftmost of the header's right-hand controls; this is the placement the owner asks for (A3).
- **What it is.** A button drawn as a text box, not an input: a 16 px magnifier 8 px in, the muted placeholder
  "Type / to search" at 14 px on a 20 px line, the `/` in a 16 x 16 key cap (12 px, 1 px muted border, 3 px corners),
  32 px tall, a 1 px border, 12rem to 24rem wide (192 to 384 px) and right-aligned (CSS G). Its accessible name says
  both what it does and the key: `aria-label="Search or jump to, type / to search"`, `aria-haspopup="dialog"`,
  `aria-expanded`; the key cap is `aria-hidden`.
- **Narrower.** From 1011.98 px down the box becomes a 32 x 32 magnifier button; opened there, the search covers the
  whole header bar.
- **Keys.** `/` and `S` both open it (GitHub's keyboard shortcuts page; measured).
- **With a query.** On the results page the box shows the current query, styled, widened to 320 px (archived
  2025-06-28), and reopening the builder keeps the query.

### 3.2 The query builder

| | Live "Quick search" (logged out) | Archived query builder (signed-in component) |
|---|---|---|
| Opens | as a modal dialog anchored to the button: top = button top - 13, right = button right + 15, width max(button + 24, 480) | as a modal dialog over the header; the signed-in CSS grows it from the box: top -14, left -14, width box + 26 |
| Size at 1280 | 480 x 272 | 1120 wide, at x = 80 |
| At 1011 px and less | a full-width sheet from the top, no top corners, at most 80% of the window | the same |
| Frame | 12 px corners, a one-pixel ring plus a soft drop shadow, a translucent backdrop, 0.2 s fade and 8 px slide (off under reduced motion) | 12 px corners, 1 px border, `0 40px 80px` shadow, backdrop |
| Input | 32 px, 6 px corners, magnifier, 14 px text, focused 1 px accent border plus a 2 px accent outline, a clear button once there is text | the same, 1094 px wide with 12 px margins |
| Qualifiers in the input | coloured by an `aria-hidden` mirror behind an input whose own text is transparent: a key plain, a value in the accent colour on an accent tint with 3 px corners, `NOT`, `OR` and a leading `-` purple | the same (`qb-filter-value`, `qb-entity`, `qb-constant`) |
| Rows | 33.5 px, 8 px side margins, 6 px corners, a 16 px icon, a 14 px label and a 12 px trailing hint on every row ("Jump to") | 34.75 px; the trailing hint says what Enter does: "Search all of GitHub", "Search in this repository", "Autocomplete" |
| Active row | a neutral tint and a 4 px accent bar 8 px outside the row's left edge | the same |
| Groups | a 12 px semibold muted heading ("Explore") | "Languages", "Values"; the group's name is also the end of each option's accessible label, since `role=group` inside a listbox is poorly supported (GitHub's QueryBuilder post) |
| Empty input | four "Explore" links | signed in: recent searches, suggested repositories, teams and projects, saved searches (docs) |
| Typing text | nothing | the query itself, then one row per scope: "lan · Search all of GitHub", "repo:microsoft/playwright lan · Search in this repository", "org:microsoft lan · Search in this organization" |
| Typing a key | nothing | `language:` gives a "Languages" group with colour dots; `language:py` matches fuzzily (Python, Papyrus, Ren'Py, Jupyter Notebook); `is:` gives "Values" |
| Keys | nothing active on open; Down activates the first row, focus stays in the input; Up on the first row does not wrap; Esc closes and returns focus to the button; Tab moves to the next focusable thing | the same, and a polite atomic live region reads "3 suggestions." |
| Foot | "Search syntax tips", 12 px accent link, over a hairline | the same, plus "Give feedback" and "Beta" when signed in (2023 release post) |
| Saved searches | none | a name (at most 50 characters) and a query (at most 500), ten at most, used as `saved:name` |

### 3.3 The results page

- **Address.** `/search?q=…&type=…&s=…&o=…&p=…`: the query, the result type, the sort field and order, the page.
  Choosing a facet rewrites `q` and drops `p`. Ten results a page; pages capped at 100.
- **Layout.** A 296 px sidebar with a 1 px divider, then the results column with 16 px 24 px padding, then (from 1012 px)
  a 340 px aside holding a sponsor box and a "ProTip!" line ("Press the / key to activate the search input again and
  adjust your query."). Fluid: no maximum width up to 1440. At 768 the sidebar narrows to 257; under 768 it goes.
- **Sidebar.** "Filter by" (16 px semibold), then one 32 px row per result type with a 16 px icon, a 14 px label and a
  count pill (20 px, 12 px semibold, abbreviated: "171k", "3.9M"; "…" while counting), zeros included; the current type
  has the neutral tint, a semibold label and the same 4 px accent bar as the builder's active row. Then "Languages" with
  10 px colour dots and "More languages…"; then "Advanced", each row a plus-circle and a qualifier's name ("Owner",
  "Number of stars", "Date created") that writes the key into the header search and opens it with the caret after the
  colon; then an "Advanced search" link. A facet row offers "Exclude {name}" on hover or focus, which adds `NOT …`.
- **Header of the results.** "171k results" (16 px, 500) and "(154 ms)" (12 px muted; seconds from 1000 ms); at the
  right "Sort by: Best match" (a 28 px button opening a menu of radio items with a check on the current one) and a
  kebab ("View search docs", "Advanced search"). A visually hidden `h2` with the count takes the focus after a search;
  focus goes back to Sort after a re-sort (GitHub's accessibility post).
- **A result.** A card: 1 px border, 6 px corners, 16 px padding, 16 px between cards. The title is a 16 px link with
  matches in bold (700); the description 14 px with matches at 600; topic pills; a 12 px muted meta row (language dot,
  stars, "Updated …") with "·" separators. Code results mark matches with a yellow `<mark>` and fold long runs behind
  "Show N more matches" (40 px, 12 px muted, a dashed top border).
- **Pages.** 32 x 32 page buttons, 6 px corners, the current one in the accent colour with `aria-current=page`,
  Previous and Next, an ellipsis.
- **Loading.** A 3 px accent progress bar along the top; the type marker moves at once; facets and results turn to
  pulsing skeletons.
- **Nothing found.** A bordered box with an illustration, "Your search did not match any repositories" (20 px
  semibold), "You could try one of the tips below." with two expandable tips, "You could try an advanced search.",
  and, when other types matched, "However we found 8.1M pull requests and 1.9M issues that matched your search query."
- **A query it cannot read.** Repository search shows no error, only no results; code search shows a warning banner
  ("Invalid repository name: …"); the issues filter bar warns without blocking ("Filter contains 1 issue: Invalid value
  … for label").
- **Under 768 px.** The sidebar and Sort go; a row shows "Filter:" with a type switcher (a menu of every type and its
  count), a "Filter" button that opens a bottom sheet titled "Filters" holding the facets, and the kebab.
- **Dark.** Only colours change; every size is the same.

### 3.4 The syntax

Whitespace is AND; `AND`, `OR` and `NOT` are keywords; parentheses group (at most five deep in the issues bar, where OR
binds loosest); quotes make an exact phrase; `-key:value` excludes; `>`, `>=`, `<`, `<=`, `a..b`, `a..*`, `*..b` are ranges;
`/regex/` in code search; a comma inside a qualifier means OR (issues); text that does not parse is searched as text
(code search docs, the issues search post).

### 3.5 GitHub's decisions, one by one, and what the report does with each

*Copy* means the report does what GitHub does; *adapt* means it does the same thing in its own terms, and the reason is
given; *leave* means it does not, and why.

| # | GitHub's decision (evidence in 3.1 to 3.4) | Here | The report |
|---|---|---|---|
| G1 | Search starts from a button drawn as a text box, leftmost of the header's right-hand controls | copy | A box-shaped button on the title line, just left of the theme switch (4.2) |
| G2 | Magnifier, "Type / to search", the key in an `aria-hidden` key cap, the key in the accessible name | copy | Same words, same parts, `aria-keyshortcuts="/"` as well (4.2) |
| G3 | 192 to 384 px wide, 32 px tall, 1 px border | adapt | 192 to 384 px wide; 34 px tall, the switch's edge, corners, ground and shadow, so the two read as one group (A2) |
| G4 | An icon button from 1011.98 px down | adapt | An icon button from 768 px down: the report has one breakpoint, pinned by `StylesheetRulesTests` (4.2) |
| G5 | `/` and `S` open the search | adapt | `/` only, as today; a second single-letter key is Q4 |
| G6 | The header box shows the current query on the results page | copy, plus | It shows the applied query, coloured; and a clear button, because here the query stays applied as the reader moves around the report (4.2, Q1) |
| G7 | A modal dialog whose input lands on the button; full-width top sheet on a narrow window | copy | Same anchoring; the sheet from 768 px down (4.3) |
| G8 | 12 px corners, ring plus shadow, translucent backdrop, 0.2 s fade and 8 px slide, none under reduced motion | adapt | 12 px corners (the report's card radius), `--kron-hairline` ring, a shadow and backdrop from `--kron-overlay`, the same motion and its reduced-motion rule (4.3) |
| G9 | Qualifiers coloured by an `aria-hidden` mirror behind a transparent-text input, never `contenteditable` | copy | The same technique (4.3.2) |
| G10 | Key plain; value accent on accent tint; operators purple | adapt | Value in `--kron-selected-ink` on `--kron-selected`; operators in `--kron-prop-key`, the report's own purple; a qualifier it cannot read in `--kron-warn` (4.3.2) |
| G11 | Combobox, listbox, `aria-activedescendant`; focus stays in the input; nothing active until Down | copy | (4.3.4) |
| G12 | Group headings visual only, the group's name in each option's label | copy | (4.3.4) |
| G13 | A trailing hint on every row saying what Enter does | copy | "Search", "Jump to", "Autocomplete" (4.3.3) |
| G14 | Active row: neutral tint and a 4 px accent bar outside its left edge; the same marker on the sidebar's current type | copy | `--kron-hover` tint, `--kron-accent` bar (4.3.3, 4.5.3) |
| G15 | Scope rows ("Search in this repository") | adapt | One scope row, the feature the reader is in, written as `feature:"…"` (4.3.3) |
| G16 | Value completion with colour dots, matched fuzzily | adapt | Values with how many scenarios carry each, status dots in the status colours; matched by word prefix then substring, the report's names being long sentences rather than short names (4.3.3) |
| G17 | A polite, atomic live region: "3 suggestions." | copy | (4.3.4) |
| G18 | "Search syntax tips" at the foot, linking to the docs | adapt | Opens the syntax inside the dialog: a report is often read offline (4.3.5) |
| G19 | Recent searches (signed in) | adapt | The searches run on this page since it opened, kept in memory only: the report stores nothing but the theme (`PersistentFilterStateReportTests`); Q5 |
| G20 | Saved searches | leave | They need storage, and the report stores nothing but the theme |
| G21 | "Give feedback", "Beta" | leave | There is no one to send it to |
| G22 | The whole state in the address: `q`, `type`, `s`, `o`, `p`; a facet rewrites `q` | copy | `q=` as today, with `tab=search`, `type=`, `s=`, `o=`, `p=` beside it (4.9) |
| G23 | A results page of its own | adapt | A Search tab in the report's bar, shown while a query is applied: the bar is where the report's places are (4.5.1) |
| G24 | 296 px sidebar, divider, 16 px 24 px padding, fluid results column | copy | (4.5.2) |
| G25 | A 340 px aside (sponsors, ProTip) | adapt | No aside: there is nothing to sponsor. The ProTip line goes under the results (4.5.6) |
| G26 | "Filter by": a row per type, counts as pills, zeros shown | copy | Scenarios, Features, Steps, Diagrams, Errors (4.5.3) |
| G27 | Facets: Languages with dots, "More languages…" | adapt | Status with status dots, Tags, Dependencies, each with "More…" (4.5.3) |
| G28 | "Exclude {name}" on a facet row | copy | Writes `-status:…`, `-tag:…`, `-dep:…` (4.5.3) |
| G29 | Advanced rows write a key into the search and open it at the colon | copy | Status, Tag, Dependency, Feature, Duration, Happy paths (4.5.3) |
| G30 | "171k results (154 ms)" | copy | "12 results (85 ms)"; the time is the search's own (4.5.4) |
| G31 | "Sort by: Best match" menu | adapt | The report's own SORT select, as the Features tools have it; Best match first (4.5.4) |
| G32 | A kebab with docs links | adapt | Search syntax tips, and the two exports, which the hidden box no longer shows (4.5.4) |
| G33 | Cards: 1 px border, 6 px corners, 16 px padding and gaps | adapt | The report's feature card: `--kron-card`, `--kron-hairline`, 12 px corners, `--kron-card-shadow` (CSS:54-66); 16 px side padding as its summary (CSS:78) and 14 px between cards as `#report-content` spaces them (CSS:74) (4.5.5) |
| G34 | Matches bold in titles and text; code matches in a yellow `<mark>` | adapt | Bold in names and steps; `<mark>` in `--kron-warn-soft` in payload, diagram and error lines (4.5.5) |
| G35 | "Show N more matches" | copy | (4.5.5) |
| G36 | Ten a page; 32 px page buttons; Previous, Next, an ellipsis; 100 pages at most | copy | Ten a page (Q6); no page cap, nothing being limited by a server (4.5.6) |
| G37 | A hidden heading with the count takes the focus after a search; Sort keeps it after a re-sort | copy | (4.5.4, 4.11) |
| G38 | Loading: a thin top progress bar, skeletons | adapt | Instant results come at once, so no skeletons; the bar runs while deep search is still reading payloads (4.5.7) |
| G39 | Nothing found: illustration, the type's name, tips, other types that matched | copy | An inline magnifier drawing in the theme's colours, no image file (4.5.8) |
| G40 | A query it cannot read: a warning that does not block (code search, issues) | copy | The qualifier is marked in the input and a warning line names it (4.3.2, 4.5.4) |
| G41 | Under 768 px: a type switcher, a "Filter" button and a bottom sheet | adapt | The same, the button called "Refine", since the bar's Filters is the report's filter box (4.5.9, Q7) |
| G42 | Dark changes colours only | copy | Every size is the same in both themes, as the report's tokens already arrange |
| G43 | `AND`, `OR`, `NOT` keywords; `/regex/`; ranges; `-key:value`; comma means OR | adapt | `-key:value`, ranges (in `duration:` already) and commas (in `dep:` already) are copied; `&&`, `\|\|`, `!!` stay the operators; words and regex are Q8 (4.4) |

## 4. Design

Wireframes of every surface, at 1280 and 390, are in Appendix B; S0 (7.2) draws them on real reports before anything
is built.

### 4.1 One query, three surfaces

The report keeps one query, as it has one today: the text `q=` carries, which narrows the report through the `sr` and
`q` flags exactly as the search line does now (Q1, recommended). Three surfaces hold it:

1. **the search button** shows it (4.2);
2. **the query builder** writes it (4.3);
3. **the Search tab** presents what it matched, laid out as GitHub's results page is (4.5).

Running a search (Enter in the builder) applies the query and opens the Search tab. The Features list, the timeline, the
components and both exports follow the applied query as they follow it today; the bar's readout says "Filtered" while it
narrows anything, and Clear All clears it with the filters. The box's filters narrow the Search tab's results as they
narrow every tab that follows the filters, and the tab says when they hide some (4.5.11).

Why the query keeps narrowing the report rather than only feeding the Search tab: every `q=` link written so far keeps
opening what it opened (the address is documented, wiki `Generated-Reports.md:759-769`); the exports, the timeline and
the components keep a text filter, which nothing else gives them; and the 24 test files that type a search keep their
meaning, so the change to them is a helper (6.2). GitHub's results page has nothing beside it to narrow; the report has,
and taking the text filter away from it is a loss the ask did not name. Q1 sets out the other answer and what it removes.

**The applied query and the draft.** The builder's input holds a draft; the applied query stays where it lives today,
in `#searchbar`, which becomes a hidden field (`<input type="hidden" id="searchbar" autocomplete="off">`). Every reader
and writer of it keeps working unchanged: `url_hash_filters` (UH:141-142), `reset_filters` (EXP:6-7), `apply_url_hash`
(UH:201-206), `run_search_scenarios` (RSF:170-171) and the performance bench that sets the value and runs the search
from script (BrowserRenderWorkerTests.cs:169-174). Enter copies the draft into it and calls `run_search_scenarios()`;
Escape throws the draft away. A draft is therefore never written to the address by a filter change made while it is
open, which keeping the draft in `#searchbar` would do. The two places that put the focus in the field after clearing
the filters (the empty state's and the filtered note's Clear All, FB:296-301, 304-310) put it on the search button.

### 4.2 The search button

**Markup** (RG:1766, written by a new `SearchButton` constant beside `ThemeSwitchButton`, RG:92-100):

```html
<div class="title-line">
  <h1>…</h1>
  <div class="title-actions">
    <span class="search-box">
      <button type="button" class="search-button" id="search-button" aria-haspopup="dialog" aria-expanded="false"
              aria-controls="search-dialog" aria-keyshortcuts="/" aria-label="Search or jump to, type / to search"
              title="Search this report, or jump to a scenario or a feature (/)">
        <svg class="search-icon" viewBox="0 0 16 16" aria-hidden="true">…magnifier…</svg>
        <span class="search-button-hint">Type <kbd aria-hidden="true">/</kbd> to search</span>
        <span class="search-button-query" hidden></span>
      </button>
      <button type="button" class="search-clear" id="search-clear" aria-label="Clear the search"
              title="Clear the search; the filters stay as they are" hidden>…x-circle…</button>
    </span>
    <button type="button" class="theme-toggle" …>…</button>
  </div>
</div>
```

- **Why its own classes.** The theme script flips the theme on any click inside `.closest('.theme-toggle')`
  (`report-theme-switch-script.js:37-39`) and `ThemeTests` finds the switch as `button.theme-toggle`
  (`ThemeTests.cs:14`), so the search shares the switch's look through a shared selector, never its class. The clear
  button sits beside the search button inside the box, since a button cannot hold a button.
- **Placement.** `.title-actions` is a flex group with an 8 px gap (the gap between GitHub's header controls), at the
  line's right end; the line keeps `space-between`, so the title stays left and the group right (a third child of a
  `space-between` row would sit in the middle). The search is the leftmost of the right-hand controls (A3, G1).
- **The box** (from 768.02 px): `flex: 0 1 384px; min-width: 192px` (GitHub's 12rem to 24rem, G3), 34 px tall, the
  switch's 1 px `--kron-hairline` edge, 8 px corners, `--kron-card` ground and `--kron-card-shadow` (CSS:22-26), so the
  two read as one group. The button fills it: a 16 px magnifier 10 px in, drawn with the switch's stroke (`.theme-icon`:
  `stroke: currentColor; stroke-width: 1.8`, round caps), in `--kron-ink-soft`; the hint at `--kron-fs-body` (14 px) on a
  20 px line in `--kron-faint`, the placeholder colour of today's field (CSS:249-268), starting 34 px in; the key cap in
  the report's existing `kbd` look (`--kron-code-bg`, `--kron-control-border`, 3 px corners, CSS:390-397) at
  `--kron-fs-meta`. Hover: `--kron-hover`, as the switch (CSS:27). Focus: the switch's ring, 2 px `--kron-accent` at
  2 px (CSS:28).
- **With a query applied** (G6): the hint hides and the query shows instead, in `--kron-ink`, on one line with an
  ellipsis, its qualifiers coloured as in the builder (4.3.2); the clear button shows at the right end inside the box
  (24 x 24, a 16 px x-circle in `--kron-muted`, after a 1 px x 16 px `--kron-hairline-soft` divider, GitHub's trailing
  action). Clear empties the query and nothing else, as one history entry (4.9), and puts the focus on the search button.
- **On a phone** (768 px and less, the report's one breakpoint, G4): the box is 34 x 34, the magnifier alone, the switch's
  exact box; with a query applied it takes the selected look the bar's Filters has when on (`--kron-selected` ground,
  `--kron-selected-edge`, `--kron-selected-ink`, CSS:883-884), and the clear button lives in the builder only. The title
  wraps beside the two buttons as it wraps beside the switch today (M1, 390 px).
- **Name and title** (D49: every control has both): the name says what it does and the key, as GitHub's does (G2); the
  title says what it searches.
- **Pages.** TestRunReport and Specifications get it; the labs page does not (it has no index and nothing to search,
  `LabsReportGenerator.cs:16-21`), so its title line, pinned by `ThemedPagesHtmlTests.cs:107-126`, is unchanged; an export
  has no title line (EXP:132).

### 4.3 The query builder

#### 4.3.1 Frame and placement

- **Element.** A native `<dialog id="search-dialog" class="search-dialog" aria-labelledby="search-dialog-title">` opened
  with `showModal()`: the top layer, the page behind it inert, the focus kept inside, Escape handled (G7, G8). Its first
  child is a visually hidden `<h2 id="search-dialog-title">Search this report</h2>`.
- **Not a layer.** Opening it writes no history entry, as the search help, the context menu and the gears write none
  (BACK_BUTTON_NAVIGATION_PLAN.md:316-336: "The context menu, the search help and the gears are not layers"); any
  arrival (Back, Forward, a link followed) closes it. It is the search help's successor, an input, not a place.
- **The backdrop is the dialog itself.** Older engines give `::backdrop` no custom properties (it inherited from nothing
  until a 2023 change to the CSS spec), so the dialog element fills the window (`inset: 0; width: 100%; height: 100%; max-width: none;
  max-height: none; padding: 0; border: 0`) with `--kron-overlay` as its ground (CSS's colour rule allows only tokens,
  `ReportSheetColoursTests.cs:84-93`), and the visible panel `.search-panel` is placed inside it; a click on the dialog
  outside the panel closes it (GitHub's backdrop click).
- **Where the panel sits** (from 768.02 px, G7): its right edge 14 px right of the search box's right edge, its top 14 px
  above the box's top, at least 8 px from the window's top (the box can be scrolled away when `/` opens it), so the
  input inside it lands on the box (GitHub's signed-in builder grows from its box by 14 px on each side); 720 px wide or
  the window less 32 px, whichever is smaller, growing leftwards over the title; at most the window's height less its
  top less 16 px. The width is settled in S0 on real reports (GitHub's archived builder is 1120 px at 1280, its live
  quick search 480 px; the report's longest scenario names decide).
- **On a phone** (G7): a sheet from the top edge, the window's full width, no top corners, at most 80% of the window's
  height (`80dvh`).
- **Look** (G8): `--kron-card` ground, a 1 px `--kron-hairline` ring, 12 px corners (the report's card radius), a shadow
  `0 12px 32px var(--kron-overlay)`; the panel fades in and slides 8 px down over 0.2 s
  (`cubic-bezier(0.3, 0.8, 0.6, 1)`, GitHub's), with no motion under `prefers-reduced-motion: reduce`.

#### 4.3.2 The input

- A 34 px field with the look of today's search field (`--kron-field` ground, 1 px `--kron-select-border`, its corners,
  and when focused the card ground, the accent edge and the 2 px accent ring, CSS:249-268), a 16 px magnifier 10 px in,
  `--kron-fs-body` text, the placeholder "Search or jump to…" (GitHub's), `spellcheck="false" autocomplete="off"`,
  inside a 12 px padded row (G8). A clear button (24 x 24, x-circle, named "Clear") shows once there is text and puts
  the focus back in the input.
- **Qualifiers coloured** (G9, G10): an `aria-hidden` mirror element sits exactly under the input's text box (same font,
  padding and letter spacing, `white-space: pre`, scrolled with the input), and the input's own text is transparent
  with a `--kron-ink` caret. A key is plain `--kron-ink`; a value (`failed` in `status:failed`, a tag, a dependency) is
  `--kron-selected-ink` on `--kron-selected` with 3 px corners; an operator (`&&`, `||`, `!!`, `not`, a leading `-`) is
  `--kron-prop-key`, the report's own purple (TOK:62, 179; GitHub's is purple too); a qualifier the report cannot read
  is `--kron-warn` (and the field's edge turns `--kron-warn`, as `kron-q-bad` does today, CSS:268). Until the mirror is
  drawn, and while an input method is composing (`compositionstart` to `compositionend`), the input shows its own text,
  so nothing typed is ever invisible.

#### 4.3.3 The suggestions

A listbox under the input, at most 20 rows' height (GitHub's 20em), in groups, in this order:

1. **The search** (no heading): "magnifier {draft}" with the trailing hint "Search" (GitHub's "Search all of GitHub"). When
   the reader is inside a feature (the scenario the arrow keys picked, or the one nearest the top of the window, as
   `kronikol_nav` finds it), a second row writes the scope: `feature:"Pancakes Creation" {draft}`, "Search in this
   feature" (GitHub's "Search in this repository", G15).
2. **Jump to**: up to five scenarios and then three features whose names hold every word typed: names with a word
   that starts with what was typed first, then the rest, each in report order. A scenario row has its status glyph in its status colour, the name with the
   matched letters at 600, its feature's name in `--kron-muted` after it, and "Jump to". Choosing one goes to it
   (`kronikol_nav.go`, the Features tab, the scenario opened, one history entry) and leaves the applied query as it was.
3. **Narrow by** (while the word at the caret starts a key, or the input is empty): `status:` "Passed, failed, skipped
   or bypassed", `tag:` "A tag the scenario or its feature has", `dep:` "A dependency the scenario calls", `feature:`
   "Part of the feature's name", `duration:` "The scenario's own time, such as >2s or >p90", `is:happy` "Happy paths";
   each "Autocomplete". Choosing one writes the key at the caret and shows its values.
4. **Values** (while the caret follows `key:`), headed by the key's name ("Statuses", "Tags", "Dependencies",
   "Features", "Durations"), filtered by what follows the colon (a word's start first, then anywhere in it, G16), each
   with the number of scenarios that carry it in `--kron-muted` and "Autocomplete": the statuses with status dots
   (10 px, GitHub's language dots), the tags and dependencies with their counts (the counts the filter box's chips
   already carry, `TagChips.Count`, RG:2008, 2022), the features' names, and for `duration:` this report's percentiles
   with their times (`>p90 (1.2 s and over)`, read from `.duration-filters[data-p90]`, RG:1990) before the forms
   `>2s`, `<500ms`, `1s..5s`.
5. **Recent** (on an empty input, Q5): the queries run on this page since it opened, newest first, at most five, kept in
   memory only (the report stores nothing but the theme, `PersistentFilterStateReportTests.cs:37-49`); "Search".

On an empty input: Recent, then "Jump to" with the failed scenarios (up to five, when the run has any: GitHub's
suggested repositories become the place a test report's reader most often goes), then "Narrow by".

Rows (G13, G14): 34 px tall, 8 px side margins, 6 px corners (the bar's buttons' radius, CSS:880), 6 px 8 px padding, a
16 px icon in `--kron-ink-soft`, the label at `--kron-fs-body` in `--kron-ink`, the trailing hint at `--kron-fs-meta` in
`--kron-muted` on every row. The active row has the `--kron-hover` ground and a 4 px `--kron-accent` bar 8 px outside its
left edge with 6 px corners. Group headings use the report's section-label look (`--kron-fs-label`, `--kron-label`,
upper case, `--kron-label-track`: "FILTERING", "SORT", "PAYLOADS" are written so), not GitHub's 12 px semibold.

#### 4.3.4 Keys and accessibility (G11, G12, G17)

- The input is `role="combobox"` with `aria-expanded`, `aria-controls="search-suggestions"`, `aria-autocomplete="list"`
  and `aria-activedescendant`, labelled by a visually hidden "Search". The list is `role="listbox"`
  `aria-label="Suggestions"`; headings are `role="presentation"`, and each option's name ends with its group ("A valid
  pancake request returns a batch, Pancakes Creation, jump to this scenario"), since `role=group` inside a listbox is
  poorly supported (GitHub's QueryBuilder post).
- Nothing is active when it opens; Down makes the first row active and the focus stays in the input; Up on the first
  row does not wrap; Enter runs the active row, or with none active runs the search; Tab on an "Autocomplete" row
  completes it, elsewhere moves the focus (the clear button, then "Search syntax tips", then back to the input); Escape
  closes it, drops the draft and puts the focus on the search button; Home and End move the caret. A pointer over a row
  makes it active; a click runs it.
- A polite, atomic live region says "5 suggestions." after each change, adding and removing a no-break space so the
  same count is read again (GitHub's).
- The dialog's own `keydown` handler stops each key it acts on, so the page's keys (`/`, the arrows, Escape) never act
  behind it.

#### 4.3.5 Search syntax tips (G18)

At the dialog's foot, over a hairline: "Search syntax tips", a button (`aria-expanded`) at `--kron-fs-meta` in
`--kron-accent`. It shows the syntax inside the dialog in place of the suggestions: today's table (RG:1929-1945)
rewritten with the forms of 4.4 first, the older forms beside them, and which parts of a scenario the instant search
reads and which only deep search reads (B3). Pressing it again, or typing, brings the suggestions back. GitHub's link
goes to its documentation; a report is often read with no network, so the tips are in the page.

#### 4.3.6 Running a search

Enter (or the search row): the draft, trimmed, goes into `#searchbar`, `run_search_scenarios()` runs (the flags, the
readout, `kron-filter-change`, a single match revealed, the deep query: all unchanged), and the reader is taken to the
Search tab, filter and place as **one** history entry (4.9). The dialog closes and the focus goes to the results'
count heading (G37). Enter on an empty draft clears the applied query, as the clear button does. Nothing applies while
typing (GitHub's; Q3 has the other answer).

### 4.4 The query language

**Kept, with the same meaning:** everything `Search-Syntax.md` documents: words, `"phrases"`, `&&`, `||`, `!!`, `not`,
parentheses, `@tag`, `@"two words"`, `$status` and the history verdicts, `dep:` and `dependency:`, `duration:`,
`is:happy`, and the legacy tag expressions. Every `q=` written by any release means what it meant.

**Added** (each a new form a reader can write, so part of the minor):

| Form | Means | Built as |
|---|---|---|
| `tag:name`, `tag:"two words"` | the same test as `@name` | a token of AS's tokenizer (AS:85-106), so it takes part in `&&`, `\|\|`, `!!` and parentheses as `@` does, in the page and in the worker |
| `status:failed` (any execution result or history verdict) | the same test as `$failed` | a token of AS's tokenizer (AS:108-120); a name that is neither a result nor one of this report's verdicts is read as text and marks the input, as an unreadable qualifier does (RSF:151) |
| `feature:text`, `feature:"two words"`, `feature:a,b` | the scenarios whose feature's name holds the text, any of several with commas | a qualifier beside `dep:` (RSF:72, 108-114): it narrows by AND |
| `-` right before any qualifier: `-tag:slow`, `-status:passed`, `-dep:payment`, `-feature:audit`, `-is:happy`, `-duration:>2s` | excludes, as `!!` before it does | AS's tokenizer for `-tag:` and `-status:`; RSF's negation group (RSF:72, `(!!\s*\|not\s+)` gains `-`) for the qualifiers |

`isAdvancedSearch` (AS:9-11) learns `tag:` and `status:` as it knows `$`; a `-` before a word that is not a qualifier
stays text, as GitHub's does (`NOT` works on words there, `-` on qualifiers). Before the forms are added, S3 measures
what the owner asked of `dep:` and `duration:` in 5.0.0 (D16: they "must not clash with real searches", measured at 0
matches): how often `tag:`, `status:` and `feature:` followed by a letter or digit occur in the `data-search` text of the
BreakfastProvider report, the example projects' reports and the test fixtures (M2).

**Not copied** (Q8): GitHub's `AND`, `OR` and `NOT` words, because the query is lower-cased before it is read (RSF:179)
and `and`, `or` and `not` are everyday words in step text, so they cannot become operators without changing what text
searches find today; and `/regex/`, which the trigram index cannot narrow, so every regex would read every payload.

**Colouring and completion** use one small pure function, `searchQuerySpans(text)`, in AS (DOM-free, Jint-tested, and
already in the worker's roster file), that cuts a query into spans: text, phrase, key, value, operator, unreadable. The
builder colours with it and finds the token at the caret with it.

### 4.5 The Search tab

#### 4.5.1 The tab and its panel

- Written on every page with a filter box (RG:2049-2056): `ReportTab("search", "Search", …)` last in the tab row,
  `hidden`, with a magnifier before its name and its count after it (`.tab-n`, CSS:800), and `ReportPanel("search", …)`
  holding the panel's frame; `report-search-results-script.js` fills it.
- It shows while a query is applied and goes when the query is cleared; if it was the tab on screen, the reader goes back
  to the tab the search was run from (G23, Q2).
- Its count is the number of scenario results; the tab keys (TABS:55-68) include it while it shows.

#### 4.5.2 Layout (G24, G25)

- From 768.02 px: two columns, the sidebar `clamp(240px, 25%, 296px)` (GitHub's 296 at 1012 and over, 257 at 768) and the
  results, 24 px apart. The sidebar is a card (the feature card's look) that sticks under the bar
  (`top: calc(var(--kron-nav-h) + 12px)`) and scrolls by itself when taller than the window (GitHub's pane does). The
  results column is fluid, as GitHub's.
- No aside: GitHub's holds a sponsor box and the ProTip; the ProTip goes under the results (4.5.6).
- 768 px and less: one column (4.5.9).

#### 4.5.3 The sidebar (G26 to G29)

- **FILTER BY** (the section-label look): one row per type, each a link that writes `type=` (4.9): Scenarios (every
  result, the default), Features (the features holding a result), Steps (results matched in their steps), Diagrams
  (results matched in their diagrams: requests, responses, messages, queries, the whole-test flow), Errors (results
  matched in an error message or stack trace). A row: 32 px, 8 px side margins, 6 px corners, 6 px 8 px padding, a 16 px
  icon, the name at `--kron-fs-body`, and the count in a pill (20 px, `--kron-fs-label` at 600, `--kron-hover` ground,
  `--kron-ink-soft`; the exact number, as the report's counts are small, with a visually hidden " results"). Zero counts
  show (G26). The current type: `aria-current="page"`, the `--kron-hover` ground, its name at 600 and the 4 px accent bar,
  the builder's active-row marker (G14). While deep search is still reading, a count that may still grow is followed by
  "…" (GitHub's "…" while counting).
- **STATUS** (GitHub's Languages): Passed, Failed, Skipped, Bypassed, each with a 10 px dot in `--kron-pass`,
  `--kron-fail`, `--kron-skip`, `--kron-bypass`, and the number of results with it in `--kron-muted` (Q9: GitHub shows no
  counts here; the report's chips carry theirs, RG:3027-3028). A status no result has is left out unless the query names
  it, as GitHub lists only the languages its results have. A click writes `status:…` into the query, or takes it out
  when it is there, and runs it (one entry); the row in the query has the current-item marker. On hover or focus an
  "Exclude" button (an x, named "Exclude Passed") writes `-status:passed` (G28).
- **TAGS** and **DEPENDENCIES**: the ten most frequent among the results, then "More tags…" or "More dependencies…" (a
  plus-circle row) that shows the rest in place; the same click and Exclude, writing `tag:"…"` or `dep:"…"`.
- **ADVANCED** (G29): plus-circle rows Feature, Tag, Dependency, Status, Duration and Happy paths. Each opens the builder
  with the key written at the end of the applied query and the caret after its colon, so its values show (Happy paths
  writes `is:happy` and runs). Last, "Search syntax tips" (GitHub's "Advanced search" link), which opens the builder on
  its tips.

#### 4.5.4 The results' header (G30 to G32, G37, G40)

- Left: `<h2 id="search-count" tabindex="-1">12 results</h2>` at `--kron-fs-feature` (16 px), weight 500, then "(85 ms)"
  at `--kron-fs-meta` in `--kron-muted`: the instant pass's time, then the whole search's once deep search ends;
  seconds from 1000 ms ("1.4 s"). "1 result" for one.
- Right: the report's SORT control (the label and select of the Features tools, RG:2070) with Best match, Report order,
  Name, Longest first, Shortest first, Failed first (4.5.10); then a 28 x 28 kebab (the bar's 6 px corners) whose menu has
  "Search syntax tips", "Export results as HTML" and "Export results as CSV" (`export_html()` and `export_csv()`, which
  export what the filters and the query leave showing, which is the results, Q1).
- A warning line when a qualifier could not be read: "`duration:abc` was searched as text: a duration is a time such as
  2s, a range such as 1s..5s, or a percentile such as p90." in `--kron-warn` on `--kron-warn-soft` with
  `--kron-warn-edge`, 6 px corners (G40: it warns and does not block, as GitHub's issues filter does).
- Focus: after a search, a type or a page, the focus goes to the count heading; after a sort it stays on the sort
  control (GitHub's accessibility post).

#### 4.5.5 A result (G33 to G35)

- **Scenarios, Steps, Diagrams, Errors**: an `<article class="search-result">` with the feature card's look (CSS:54-66),
  12 px 16 px padding, 14 px between results.
  - **Head**: the status glyph in its colour (the summary's `✗` and its kin, CSS:86-88), the feature's name in
    `--kron-muted` (a link to the feature), " / ", and the scenario's name, a link (`href="#sid-…"`) at
    `--kron-fs-feature`, weight 600, in `--kron-accent`, its matched words at 700 (G34); its own time at the right in
    `--kron-fs-meta`, `--kron-muted`, in the report's duration format.
  - **Meta** (`--kron-fs-meta`, `--kron-muted`, "·" between items, GitHub's meta row): its tags as pills
    (`--kron-selected-ink` on `--kron-selected`, 999 px corners, 0 8 px, 20 px tall: GitHub's topic pills in the
    report's selected colours), "Happy path", and for an outline "2 of 5 rows match".
  - **Where it matched** (4.6): a box (`--kron-strip` ground, `--kron-hairline-soft` edge, 8 px corners) of up to three
    lines, each a kind label (`--kron-fs-label`, upper case, `--kron-label`, an 80 px column: NAME, STEP, DIAGRAM, FLOW,
    ERROR, TAGS, EXAMPLE, DESCRIPTION) and the line at `--kron-fs-control`: steps, names and descriptions in the report's
    font with matches at 700; diagram, flow and error lines in `--kron-mono` with matches in a `<mark>` on
    `--kron-warn-soft` (GitHub's yellow mark, G34). A Steps, Diagrams or Errors result shows only its own kind's lines.
    Each line is a link to its place (4.6.5). Under them "Show 4 more matches" (`--kron-fs-meta`, `--kron-muted`, a dashed
    top border, G35) shows up to 20, then "Show less".
- **Features**: the same card with the feature's name as the link, "4 of 7 scenarios match", and up to five result rows
  (glyph, name with matches at 700, time), then "Show 2 more".

#### 4.5.6 Pages (G36)

Ten results a page (Q6). Under them a `<nav aria-label="Pagination">`: Previous, page numbers with an ellipsis, Next;
32 x 32 buttons with 6 px corners at `--kron-fs-body`; the current page on `--kron-accent` in `--kron-on-accent` with
`aria-current="page"`; a disabled Previous or Next in `--kron-faint`. A page is `p=` (4.9); changing it scrolls to the
results' top under the bar and moves the focus to the count. Under the pages, GitHub's ProTip line in `--kron-muted`:
"Press / to search again and change the query."

#### 4.5.7 While deep search reads (G38)

The instant pass answers at once, so there is no skeleton. While deep search runs after it, a 3 px `--kron-accent` bar
runs along the top of the results column (without moving under reduced motion), the counts that may grow carry "…", and the
results it adds are placed by Best match, after the instant results of the same strength (4.5.10), so a page already
being read changes at its end, not its start. This replaces the chip in the search field (RSI:472-509), whose sentences
the bar and the counts now say; `window.__kronikolSearch` keeps every field it has (RSI:468, 603-607, 658-665;
`DeepSearchTests.cs:570-592`).

#### 4.5.8 Nothing found (G39)

A result card holding a magnifier drawn inline in `--kron-dot` (no image file), "Your search did not match any
scenarios" (or steps, diagrams, errors, features) at `--kron-fs-feature`, weight 600, and when another type has results,
"However, it matched 3 steps and 1 error." with each type a link. Then GitHub's expandable tips, as `<details>`: "Search
payloads and diagrams" (deep search reads requests, responses, SQL and stack traces once a word has three letters, RSI:
215-232), "Narrow with qualifiers" (three examples), and, when the box's filters hide results, "The filters hide 4
results" with Clear filters. Last, "Search syntax tips".

#### 4.5.9 On a phone (G41)

The sidebar and the sort go. Over the results a row: the type switcher (a button "Scenarios 12" that opens a menu of
the types with their counts), "Refine" (opens a sheet from the bottom titled "Refine", 12 px top corners, sliding up over
0.25 s, holding STATUS, TAGS, DEPENDENCIES and ADVANCED; Escape and its close button close it), and the kebab. "Refine",
not GitHub's "Filter", because the bar's Filters is the report's filter box (Q7). A result's lines put their kind label
above the line.

The tab row scrolls sideways on a phone with its scrollbar hidden (CSS:790-791; at 390 px the measured report shows
"Histor…", M1) and nothing scrolls it to the current tab (TABS has no scroll call). The Search tab is last in the row and
becomes current from the builder, not from a tap, so it would be current out of sight: whenever a tab becomes current,
the row's `scrollLeft` is set to bring it into view (not `scrollIntoView`, which can move the page too).

#### 4.5.10 Best match and the other sorts

Best match orders by a score: each positive term found in the scenario's name counts 8, in its tags 4, its steps 2, its
error 2, its feature's name, its descriptions, rule, endpoint or examples 1, its diagrams or flow 1; ties keep report
order. S0 sets the weights on the BreakfastProvider report and the examples (a scenario named after the word should come
first). Report order is the Features list's order; Name sorts by name with no culture; Longest first and Shortest
first use the scenario's own time, as `duration:` reads it (RSF:99-102); Failed first puts failed, then skipped, then the
rest, each in report order. `s=` and `o=` carry a sort other than Best match (4.9).

#### 4.5.11 The results and the filter box

The results are the scenarios the query matches that the box's filters leave showing: the set the Features list shows.
When the box's filters hide some of the query's matches, a note over the results says so in the look of the timeline's
note (report-timeline-script.js:92-99): "Filtered: the filters hide 4 of this search's results.", with its Clear, which
clears the box's filters and leaves the query.

### 4.6 Where it matched

#### 4.6.1 The parts of a scenario's text

A scenario's `data-search` is its parts joined by spaces, in a fixed order (RG:2459-2472: the feature's name, its name,
the feature's description and endpoint, its description, rule, tags, `retry N`, its error message, its background and
steps, its diagram terms, its examples). The generator writes beside it `data-search-kinds`, the offset where each kind
starts in that text (a letter and a number per kind, e.g. `f0 n24 d63 t120 e180 s240 g700 x900`), on each scenario and
each outline group (RG:3532-3558). `data-search` itself is unchanged byte for byte, so the instant search, its 22
attribute facts (`SearchDataAttributeTests`), the index and its pins see exactly the text they see now. The offsets are
taken on the parts as they are lower-cased and joined, and a fact pins `data-search` byte for byte against 5.8.0's on the
rich fixture (`ToggleDefaultsBaselineTests.BuildRichFixture`), so no assumption about how lower-casing changes lengths
is made. With the kinds, the page knows for each result which of its parts matched, which is what the type counts
(4.5.3) and Best match (4.5.10) need, for every result, at no cost but a split.

#### 4.6.2 Deep kinds and lines, in the worker

- The worker joins a scenario's pieces with `'\n'` (RSI:376-388, 511-546); it now also records where each piece starts
  and what it is: the scenario's `data-search` (with its kinds), a diagram source (`#puml-data`, `data-plantuml-z`, a raw
  `pre`, the element's id as its reference), an error (`.failure-result pre`) or a flow (the flame text, its id). The
  joined text and the match are unchanged.
- For each scenario that matches, the `matches` batches (RSI:417-418) carry the kinds its positive terms hit, worked out
  after the match, for matched scenarios only.
- A new request, `snippets {gen, input, items, max}`, asks for up to `max` lines per scenario, only for the scenarios on
  the page shown (ten); the answer per scenario is how many hits it has and, per hit, its kind, its reference, the line
  around it in display form (4.6.3) and the ranges to mark. It is cancelled by the generation counter as queries are
  (RSI:407, 632).
- The new functions are pure and join the worker's roster, whose pins move (`NoteCopyFidelityScriptTests.cs:74-89`,
  `VerdictSearchTests.cs:106-119`); the corpus cache (RSI:296-298, 64 MiB) keeps what it keeps, and the display text is
  made only for the scenarios asked about, and not kept.

#### 4.6.3 Display text

The corpus is normalized and lower-cased (RSI:57-73), and a snippet should read as written. `kronNormalizeForDisplay`
runs `kronNormalizeForSearch`'s passes without pass 2 (the ASCII fold), with passes 4 and 5a matching any case, so the
two results have the same length and differ only in the case of ASCII letters: a range found in the search text is the
same range in the display text. A Jint fact checks `asciiFold(display(s)) === normalize(s)` for every input of the
shared vectors (`tests/shared-vectors/search-index-vectors.json`); the vectors, `normalize.js` and `SearchNormalizer.cs`
do not change, and the index is not involved.

A line is the piece's line holding the hit (pieces are multi-line: payloads, SQL, stack traces), cut to 160 characters
around it at word boundaries, with "…" where it is cut.

#### 4.6.4 Lines from the page

For the results on the page shown, lines of the instant kinds come from the scenario's own markup, as written: its name
(the summary), its feature's name, its descriptions, its step elements and, for sub-steps not yet drawn, the scenario's
`#step-data` entry (REPORT_FORMAT_PLAN Q8), its error `pre`, its tags, its example cells. When the index is absent
(`FullSearchIndex` off), diagram terms show from `data-search`, lower-cased, labelled DIAGRAM.

#### 4.6.5 Opening a hit

A line is a link to its scenario (`href="#sid-…"`), so `kronikol_nav` takes it as it takes every scenario link
(NAV:617-628): the Features tab, the scenario opened and scrolled under the bar, one history entry, and Back to the
Search tab where the reader was. The hit goes with the arrival, not in the address. Once there:

- **Step**: the steps section opens (and the sub-steps that hold it), the step whose text holds the hit scrolls into
  view and flashes (`--kron-selected` fading out over 1.2 s; none under reduced motion).
- **Error**: the failure section opens and its `pre` flashes; the hit is marked with the CSS Custom Highlight API where
  the browser has it, the flash alone where not.
- **Diagram or flow**: the diagrams section opens, the diagram is drawn if it was not (`_renderDiagramsInContainer`,
  TABS:24-25), the first text run holding the hit (compared in search form) scrolls into view and its note or arrow
  flashes; a hit in a note's folded part opens that note first (the note's own expand). Text the diagram does not draw
  (a header a note leaves out) scrolls to the diagram and the status line (NAV:400-413) says "Found in this diagram's
  source", with a link that opens the source.
- **Name, tags, description, example**: the scenario opens, as a link to it does today.

This is the hit location the 3.0.70 index plan left as Phase 2 (`git show 82abeb7f:plans/SEARCH_INDEX_PLAN.md`, its
section 10: "build later or never"). It is built here, for the ten results on the page shown, because results that
cannot say where they matched are not GitHub's results page (A1); its premise there, that "the verify pass already
knows which source matched", does not hold in the code (RSI:252-272 returns a boolean over joined text), which is why
4.6.2 records the pieces.

### 4.7 The filters

| # | Change | Today |
|---|---|---|
| F1 | **Hidden by default.** The box is written with `hidden` and without `rows-folded`; its search line (4.1), the phone's Filters line and the syntax help (4.3.5) leave it | RG:1924-1945 |
| F2 | **Its place is the top of the bar**, where 5.0.0's box already rides (FB:193), written there by the generator, so it never moves: at rest it is the bar's top section in the page's flow; pinned, it pins with the bar, at most 60% of the window, scrolling inside (FB:180-187, kept). The box's other home, the move into the bar and back, and closing when the bar comes to rest go (FB:94, 188-228, 252) | FB:88-263 |
| F3 | **Filters always shows**, at rest and pinned, at every width. The rule hiding it at rest (CSS:922-925) and the one hiding the group on a phone (CSS:929) go; on a phone the group takes a line of its own under the tabs (`.nav-row { flex-wrap: wrap }`), where the readout rides as it rides the box's Filters line today (FB:265-275); `fit()` counts the button at rest as well (FB:99-107) | CSS:922-929 |
| F4 | **Filters opens and closes it**, with `aria-expanded` and `aria-controls="filtering-box"`, titled "Show the filters" and "Hide the filters" (D49). Escape inside the box or the bar's filter group and the box's close button (always shown in its header now) close it. Opening puts the focus on the box (`tabindex="-1"`, named "Filters"), so the next Tab reaches its first control; closing puts it back on Filters | FB:188-238 |
| F5 | **Every row shows whenever the box does.** More filters, its count, `rows-folded`, `help-open` and the phone's own Filters button go (RG:1927, 3115-3120; CSS:273-293; FB:56-83, 330-338; RSF:3-9; `report-init-script.js:10-13`); the measured layout (D18, FB:19-54) measures when the box first shows, as it waits for the rows today (FB:33-39) | D15 |
| F6 | **Filters counts the filters on**, which More filters did: the count pill moves into the button (CSS:284-289), and its name becomes "Filters, 2 on". The readout and Clear All stay beside it on a desktop (`hooks.sync`, FB:138-154) and ride its line on a phone; while the box is open they give way to its header, which holds both, as they do today while the box rides (`.nav-filters.riding`, CSS:888), the readout hidden visually only (F8) | FB:56-83 |
| F7 | **A bar on every page with a filter box.** `useTabs` (RG:1685) holds for every TestRunReport and Specifications page; a page that had only its features gets the bar with one tab, Features, so Filters and the Search tab have a home (Q10). The mark, the focus script (RG:1686-1690) and the pinning come with it | RG:1677-1690, 2044-2045 |
| F8 | **The live region moves to the bar.** The bar's readout, always in the page, becomes `role="status"`; the box's copy becomes `aria-hidden` (the reverse of RG:3123-3128); `describe()` (NAV:434-437) reads the bar's. A hidden box silenced every announcement otherwise. While the box is open the bar's readout is clipped out of sight, never `display:none` (CSS:888 hides it so today), so the region speaks with the box open as well as closed | RG:1925, 3128; CSS:888 |
| F9 | **Exports.** The box keeps Export Filtered HTML and CSV in its header; the Search tab's kebab has both too (4.5.4), so they stay one click from the results | RG:1925 |

What the reader sees: at the top of the page the title line, the run band and the bar, with no box between them (on the
measured report the bar starts 111 px higher at 1280: the box's 97 px and its 14 px margin, M1); Filters at the bar's
right at all times, with the count of filters on; pressed, the box opens at the bar's top with every row showing;
pressed again, or Escape, or its close button, it goes. The rule the 5.0.0 preview recorded from the owner for the box
in the bar still holds: it "pins with the bar, covers nothing, and leaves no gap where it was"
(`OPTION_C_PLAN.harness/mock/c-round8.js:2-8`).

### 4.8 Keys

| Key | Where | Does | Today |
|---|---|---|---|
| `/` | anywhere but a field, with no lightbox, flow popup or dialog open, on a page with the search | opens the builder | focuses `#searchbar` (KN:17-21) |
| `/` | an exported page, or with a layer open | nothing; the key is left to the browser | swallowed (B7) |
| Down, Up | the builder's input | the active suggestion | |
| Enter | the builder's input | runs the active suggestion, or searches | flushes the field (RSF:59) |
| Tab | the builder, on an "Autocomplete" row | completes it | |
| Escape | the builder | closes it, focus to the search button | |
| Escape | the box or the bar's filter group | closes the box, focus to Filters | puts the box back above the bar (FB:236-238) |
| Down, Up | the Search tab's panel | scroll the page | would switch to the Features tab (TABS:69-72, KN:26-41): both skip `#panel-search` |
| Left, Right, Home, End | the report tabs | as today, the Search tab with them while it shows | TABS:55-68 |

### 4.9 The address and Back and Forward

- `q=` is unchanged, written from `#searchbar` (UH:141-142).
- New place keys, beside `tab=` and `view=` (NAV:9-13, 49-61): `tab=search` (a new value of a documented key), `type=`
  (`features`, `steps`, `diagrams`, `errors`; Scenarios is the default and not written), `s=` (`name`, `duration`,
  `status`, `order`; Best match is the default) with `o=` (`asc`, `desc`) where the sort has both, and `p=` (2 and up).
  GitHub's names (G22). Written only when not the default, as `tab=` and `view=` are.
- Entries, by the 5.2.0 rules (BACK_BUTTON_NAVIGATION_PLAN rules 1 to 4): running a search from the builder is one entry,
  the filter and the place together (`kronikol_nav.go` gains the filters an arrival carries, as `apply_url_hash` takes
  them on an arrival, NAV:482-488); a facet, an Exclude or Clear is one entry (one filter control); a type, a sort or a
  page is one entry each (a place); a result followed is one entry (a place). Back from a scenario returns to the Search
  tab with its type, sort and page, scrolled where it was, the focus on the result followed (NAV's `address()` finds it
  by its `href`, NAV:92-108). Opening the builder or the Refine sheet is no entry; an arrival closes both.
- An old link with `q=` and no `tab=` opens the Features tab filtered, as today.
- Clearing the search while the Search tab shows takes the reader to the tab it was run from, in the same entry.

### 4.10 Other pages and paths

- **Specifications** gets all of it (it is drawn by the same code, RG:532). The violet sheet's rules on `.filtering-box`,
  `.filter-search` and `#searchbar` (`Constants/Stylesheets.cs:124-127, 132`) move to the new classes; the rest follows
  the tokens.
- **Labs**: unchanged (4.2).
- **Export Filtered HTML** has no title line, bar or box (EXP:119-135): the new scripts start only when their markup is
  in the page, as FB does (FB:289-290), and `/` leaves the key alone there (B7).
- **The compressed page** (5.6.0): the new scripts start on the page the loader writes, as every other does.
- **An Azure DevOps viewer** (`<iframe srcdoc>`): every history write goes through `kronikol_nav`, which already writes
  there safely (5.2.0, S21); `showModal()` works inside an iframe.
- **`kronikol merge`, `ingest` and the CLI**: no new option, so nothing to pass; a merged report is drawn by the same
  code (`Merge/MergeableReportRenderer.cs:93`).
- **Custom CSS and themes**: every new rule uses existing tokens (4.2 to 4.7 name them); no token is added.
- **Kronikol4J**: its page is 3.0.43's (`Kronikol4J/README.md:9-11`); nothing is ported, and the ledger gets an entry
  (7.5).

### 4.11 Accessibility

Besides 4.2, 4.3.4, 4.5.3, 4.5.4 and F4, F8: every new control has a name and a title (`ControlTitlesAuditTests` sweeps
them); the marks, pills and the active-row marker are checked for contrast in both themes (text at least 4.5:1, the
marker 3:1), by a fact that reads the painted colours; the results are a list of `article`s under the count heading, and
each type, page and facet control says its state (`aria-current`, `aria-pressed`, `aria-expanded`).

### 4.12 Cost

- **Budgets**, on the 2,000-scenario report the index plan measured (`tools/search-bench`, `formatter-probe --corpus
  2000`), in Chromium, contention-scaled as `DeepQueryMs` is (`BrowserRenderWorkerTests.cs:166-174`, `ContentionScale.cs:
  32-39`): the builder open within 50 ms; a keystroke's suggestions within 16 ms; Enter to a painted page of results with
  its instant lines within 200 ms; a page's deep lines within 300 ms once its corpora are cached; `DeepQueryMs` unchanged.
  Two new guards, `SearchOpenMs` and `SearchResultsMs`.
- **Weight**: the new scripts and rules add an estimated 40 to 50 KB to every report before compression; S4 and S6
  measure it on the BreakfastProvider report and record it, plain and compressed.

### 4.13 Files

| File | Change |
|---|---|
| `ReportGenerator.cs` | the title line (RG:1766); the box (RG:1912-2030): hidden, no search line, no phone line, no help, no More filters; the bar (RG:2041-2058): on every page, the box at its top, Filters with its count, the bar's readout as the live region; the Search tab and panel; `data-search-kinds` (RG:2459-2474, 3532-3558) |
| `report-search-ui-script.js` (new) | the search button and the builder (4.2, 4.3) |
| `report-search-results-script.js` (new) | the Search tab (4.5) and opening a hit (4.6.5) |
| `search-styles.css` (new) | the rules of 4.2 to 4.6, tokens only |
| `advanced-search.js` | `tag:`, `status:`, `-` (4.4); `searchQuerySpans` |
| `report-search-function.js` | `feature:` and `-` for qualifiers; the kinds of a match; B5, B6; the chip's code goes to the results script |
| `report-search-index.js` | piece boundaries, kinds in `matches`, `snippets`, `kronNormalizeForDisplay` (4.6) |
| `report-filter-bar-script.js` | F2 to F6 |
| `report-keyboard-navigation-function.js`, `report-tabs-script.js` | `/` (4.8, B7); the Search tab's arrows (4.8) |
| `report-navigation-script.js` | the search place keys and the filters an arrival carries (4.9) |
| `stylesheets.css`, `Constants/Stylesheets.cs` | the box, the bar, the title line; violet's rules (4.10) |
| `report-export-function.js` | B6 |

New resource names must not end with an existing one's, nor an existing one with theirs (`LoadResource` matches by the
end of the name, RG:76-85; every `report-*.js` is embedded, `Kronikol.csproj:96`): none of the three does.

### 4.14 What does not change

The query's meaning and every `q=` link; the index's format, its vectors, its three normalizers and its pins
(`SearchIndexReportTests`, `CompressedPageTests.cs:305-319`); `data-search`; the theme-only storage rule; the six-flag
visibility combiner (FMH:64-90) and `kron-filter-change`; the exports' content; the filter rows' controls and their
behaviour (the chips' click cycle is the sibling plan's, 7.4); Back and Forward's rules; the labs page.

## 5. Defects found on the way

Each was read in the code at `29e551f3` (the line given) and is fixed by the release, with a fact that fails on 5.8.0
(6.1). B1, B2 and B7 are fixed by the new input and keys as they are built; the others are fixes of code that stays.

| # | Defect | Where | Fix |
|---|---|---|---|
| B1 | **Enter runs every search twice.** Enter's `keydown` runs it at once (`flush_search`), then the same key's `keyup` reaches `onkeyup="search_scenarios()"` and runs it again 300 ms later: a second readout, a second deep query (cancelling the first), a second address write and a second click on the first matching outline row | RSF:36-61, RG:1926 | The builder runs a search on Enter only, once (4.3.6) |
| B2 | **A pasted or dropped query waits until the field loses focus**, as does text from an input method that sends no `keyup`: the field listens for `keyup` and `change`, never `input` | RSF:51-61, RG:1926 | The builder listens for `input` (4.3.2) |
| B3 | **The help says the instant search reads "diagram source"** ("Text search (feature name, scenario name, step text, tags, diagram source)"); it reads the participants' names and the URLs of HTTP calls (`ParticipantRegex`, `UrlRegex`), and the rest of a diagram only deep search reads. The wiki says the same | RG:1932, 7103-7131; wiki `Search-Syntax.md:11` | The tips say which part each search reads (4.3.5); the wiki line is corrected |
| B4 | **A scenario skipped after a failure is counted three ways**: as skipped by the run band, as failed by the Status buttons, and as neither by `$status`, so `$failed` and `$skipped` both miss a scenario the Failed button shows | RG:1781; `report-status-filter-function.js:22`; AS:284 | `status:` and `$status`, and the Search tab's Status facet, follow the Status buttons (Q11) |
| B5 | **Emptying the search leaves the outline rows it marked** (`row-search-match`) **and the row it selected**: the empty-query path returns before the clean-up | RSF:267-276 (clean-up at 252, 333) | The empty path clears them |
| B6 | **Clear All, and Back to a page with no search, leave open what a single-match search opened**: `reset_filters` never calls `close_opened_by_search`, which only an emptied field calls | EXP:1-30; RSF:18-34 | `reset_filters` closes what the search opened, and only that (5.2.0's rule, BACK S8, kept) |
| B7 | **`/` swallows the key where there is no search field** (an exported page: `preventDefault` comes before the look-up), and **puts the focus behind an open lightbox or flow popup** | KN:17-21 | `/` acts only where it opens the builder (4.8) |
| B8 | **`SearchFunctionTests` (37 facts) test a copy of the search, not the search**: `tests/Kronikol.Tests/Reports/SearchFunction.cs` is a C# port that still hides with a `search-hidden` class (its line 14); the page hides with inline `display:none` (FMH:68-69). They pass whatever the shipped script does | `SearchFunction.cs`, `SearchFunctionTests.cs` | Each fact is moved onto the shipped `report-search-function.js` under Jint (it has loaded without a `document` since 5.2.0, `Kronikol.Tests.SearchEngine`), and the copy is deleted |
| B9 | **The wiki's address grammar names `parse_url_hash`**, gone since 5.2.0 (`apply_url_hash`, `UrlHashFilterTests.cs:111`) | wiki `Report-Configuration.md:285-293` | Corrected with 4.9's keys |

Checked and not a defect: whether the bar refits its row when Filters appears as it floats (FB:99-107 runs only on a
sync, a tab click or a resize). On the live report at 780 to 1100 px the tabs never overflow, before or after
(`current/probe-fit.js`), and F3 makes Filters a constant of the row anyway.

## 6. Tests

Facts are written first and run against 5.8.0 before any code (6.4). "Red on 5.8.0 because" says what fails there; a
fact marked *guard* passes on 5.8.0 and must keep passing. E2E classes are Playwright in `tests/Kronikol.Tests.EndToEnd`
on the bundled browsers; Jint classes in `tests/Kronikol.Tests.SearchEngine` run the shipped scripts; unit classes in
`tests/Kronikol.Tests/Reports`.

### 6.1 New facts

**Defects (S1)**

| # | Fact | Class | Red on 5.8.0 because |
|---|---|---|---|
| B1 | `Enter_runs_one_search` (a counter wrapped round `run_search_scenarios` from the test; the report's search input, whichever it is) | E2E `SearchBuilderTests` | the counter reads 2 |
| B2 | `A_pasted_query_is_read_without_leaving_the_field` | E2E `SearchBuilderTests` | nothing happens until blur |
| B3 | `The_syntax_tips_say_what_the_instant_search_reads` | unit `SearchTipsMarkupTests` | the help says "diagram source" |
| B4 | `Status_failed_finds_a_scenario_skipped_after_a_failure` (Jint and E2E) | Jint `SearchQueryFormsTests`, E2E `SearchResultsTests` | `$failed` misses it |
| B5 | `Emptying_the_search_unmarks_its_outline_rows` | E2E `SearchResultsTests` | the rows keep `row-search-match` |
| B6 | `Clear_All_closes_what_a_single_match_opened`, `Back_to_no_search_closes_what_it_opened` | E2E `BackForwardFilterTests` | the feature stays open |
| B7 | `Slash_is_left_to_the_browser_on_an_exported_page`, `Slash_does_nothing_under_a_lightbox` | E2E `SearchBuilderTests` | the default is prevented; the focus moves behind the lightbox |
| B8 | the 37 facts of `SearchFunctionTests`, each on the shipped script | Jint `ShippedSearchFunctionTests` | (a move: the proof is that each runs the page's code; one mutation per family, 6.4) |

**The button and the builder (S2)**

| # | Fact | Class | Red on 5.8.0 because |
|---|---|---|---|
| T1 | `The_title_line_holds_the_title_then_the_search_then_the_theme_switch` | unit `SearchButtonMarkupTests` | there is no search button |
| T2 | `The_search_box_has_the_switch_s_height_edge_corners_ground_and_shadow` (computed styles, both themes) | E2E `SearchButtonTests` | no box |
| T3 | `The_search_box_is_192_to_384_px_wide_from_768_02_px_and_the_switch_s_square_below` | E2E `SearchButtonTests` | no box |
| T4 | `The_search_box_and_the_switch_share_their_top_and_bottom_at_every_width` (320 to 1400) | E2E `SearchButtonTests` | no box |
| T5 | `The_search_button_names_its_key_and_says_it_opens_a_dialog` | unit `SearchButtonMarkupTests` | no button |
| T6 | `An_applied_query_shows_in_the_search_box_with_its_values_coloured` | E2E `SearchButtonTests` | no box |
| T7 | `Clear_empties_the_query_and_nothing_else_in_one_entry` | E2E `SearchButtonTests` | no clear |
| T8 | `The_labs_title_line_is_unchanged` | unit `ThemedPagesHtmlTests` | guard |
| QB1 | `Clicking_the_search_box_opens_the_builder_with_the_focus_in_its_input` | E2E `SearchBuilderTests` | no builder |
| QB2 | `Slash_opens_the_builder_from_a_tab_and_a_scenario_but_not_from_a_field` | E2E `SearchBuilderTests` | `/` focuses the field |
| QB3 | `The_builder_s_input_lands_on_the_search_box_and_is_a_top_sheet_on_a_phone` (geometry at 390, 768, 769, 1012, 1280, 1440) | E2E `SearchBuilderTests` | no builder |
| QB4 | `The_page_behind_the_builder_is_inert_and_a_click_outside_closes_it` | E2E `SearchBuilderTests` | no builder |
| QB5 | `Typing_lists_scenarios_and_features_to_jump_to` | E2E `SearchBuilderTests` | no builder |
| QB6 | `Jumping_opens_the_scenario_and_leaves_the_applied_query` | E2E `SearchBuilderTests` | no builder |
| QB7 | `Down_activates_the_first_row_and_the_focus_stays_in_the_input` (`aria-activedescendant`) | E2E `SearchBuilderTests` | no builder |
| QB8 | `Enter_with_no_active_row_runs_the_search_and_opens_the_Search_tab_in_one_entry` | E2E `SearchBuilderTests` | no builder |
| QB9 | `A_partial_key_offers_the_key_and_Tab_completes_it` | E2E `SearchBuilderTests` | no builder |
| QB10 | `After_status_colon_the_statuses_show_with_their_counts` | E2E `SearchBuilderTests` | no builder |
| QB11 | `After_tag_colon_the_tags_show_with_their_chips_counts` | E2E `SearchBuilderTests` | no builder |
| QB12 | `After_duration_colon_this_report_s_percentiles_show_with_their_times` | E2E `SearchBuilderTests` | no builder |
| QB13 | `Escape_closes_the_builder_drops_the_draft_and_focuses_the_search_box` | E2E `SearchBuilderTests` | no builder |
| QB14 | `The_live_region_counts_the_suggestions` | E2E `SearchBuilderTests` | no builder |
| QB15 | `The_syntax_tips_open_in_the_builder_and_typing_brings_the_suggestions_back` | E2E `SearchBuilderTests` | the help is in the box |
| QB16 | `Keys_in_the_builder_never_reach_the_page` (arrows move no scenario, Escape deselects no diagram) | E2E `SearchBuilderTests` | no builder |
| QB17 | `The_builder_writes_no_entry_and_an_arrival_closes_it` | E2E `SearchBuilderTests` | no builder |
| QB18 | `Recent_lists_this_page_s_searches_and_nothing_is_stored` (storage read before and after) | E2E `SearchBuilderTests` | no builder |
| QB19 | `Under_reduced_motion_the_builder_does_not_move` | E2E `SearchBuilderTests` | no builder |
| QB20 | `A_draft_never_reaches_the_address` (type, Escape, press a status chip: `q=` absent) | E2E `SearchBuilderTests` | typing writes `q=` |
| QB21 | `Composed_text_stays_visible_while_an_input_method_composes` | E2E `SearchBuilderTests` | no builder |
| QB22 | `The_builder_runs_a_search_in_Firefox_and_WebKit` (local legs) | E2E `SearchBuilderEngineTests` | no builder |

**The language (S3)**

| # | Fact | Class | Red on 5.8.0 because |
|---|---|---|---|
| L1 | `Tag_colon_is_at_tag`, with quotes and with `&&`, `\|\|`, `!!` and parentheses | Jint `SearchQueryFormsTests` | `tag:` is text |
| L2 | `Status_colon_is_dollar_status_for_results_and_verdicts` | Jint `SearchQueryFormsTests` | `status:` is text |
| L3 | `An_unknown_status_name_is_text_and_marks_the_input` | Jint, E2E | it matches nothing |
| L4 | `Minus_before_a_qualifier_excludes_it` (each of tag, status, dep, feature, is, duration) | Jint `SearchQueryFormsTests`, `SearchQualifierPlacementTests` | `-` is text |
| L5 | `Minus_before_a_word_is_text` | Jint | guard |
| L6 | `Feature_colon_keeps_the_scenarios_whose_feature_name_holds_it` (and commas) | `SearchQualifierPlacementTests` | `feature:` is text |
| L7 | `New_forms_inside_quotes_are_text` | Jint | guard (`"tag:x"` is text today and stays text) |
| L8 | `Every_documented_query_finds_what_it_found_in_5_8_0` (a table of the wiki's examples and their 5.8.0 results over the rich fixture, captured on 5.8.0 and pinned) | Jint `SearchCompatibilityTests` | guard |
| L9 | `searchQuerySpans_cuts_keys_values_operators_and_unreadable_qualifiers` | Jint `SearchQuerySpansTests` | no function |
| L10 | `The_worker_reads_the_new_forms_as_the_page_does` | Jint `VerdictDeepSearchTests` | the worker reads them as text |

**The Search tab (S4)**

| # | Fact | Class | Red on 5.8.0 because |
|---|---|---|---|
| R1 | `The_Search_tab_shows_while_a_query_is_applied_and_goes_with_it` | E2E `SearchResultsTests` | no tab |
| R2 | `Clearing_on_the_Search_tab_returns_to_the_tab_the_search_was_run_from` | E2E `SearchResultsTests` | no tab |
| R3 | `Filter_by_lists_every_type_with_its_count_zeros_included` | E2E `SearchResultsTests` | no tab |
| R4 | `The_type_counts_match_where_each_result_matched` (a fixture whose step, diagram and error matches are known) | E2E `SearchResultsTests` | no tab |
| R5 | `A_type_lists_only_its_results_and_writes_type_to_the_address` | E2E `SearchResultsTests` | no tab |
| R6 | `The_current_type_has_the_accent_bar_and_aria_current` | E2E `SearchResultsTests` | no tab |
| R7 | `A_status_row_writes_status_into_the_query_and_a_second_press_takes_it_out` | E2E `SearchResultsTests` | no tab |
| R8 | `Exclude_writes_the_minus_form` | E2E `SearchResultsTests` | no tab |
| R9 | `More_tags_shows_the_rest_in_place` | E2E `SearchResultsTests` | no tab |
| R10 | `An_advanced_row_opens_the_builder_at_its_values` | E2E `SearchResultsTests` | no tab |
| R11 | `The_header_shows_the_count_and_the_time` | E2E `SearchResultsTests` | no tab |
| R12 | `Longest_first_orders_by_own_time_and_writes_s_and_o` | E2E `SearchResultsTests` | no tab |
| R13 | `Best_match_puts_a_scenario_named_after_the_word_first` | E2E `SearchResultsTests` | no tab |
| R14 | `Ten_results_a_page_and_each_page_writes_p` | E2E `SearchResultsTests` | no tab |
| R15 | `Back_from_a_result_returns_to_its_page_type_sort_and_scroll_with_the_focus_on_it` | E2E `BackForwardSearchTests` | no tab |
| R16 | `A_result_shows_its_feature_its_time_its_tags_and_up_to_three_lines` | E2E `SearchResultsTests` | no tab |
| R17 | `Show_more_lists_up_to_twenty_and_Show_less_folds_them` | E2E `SearchResultsTests` | no tab |
| R18 | `Nothing_found_names_the_type_and_the_types_that_did_match` | E2E `SearchResultsTests` | no tab |
| R19 | `An_unreadable_qualifier_is_warned_about_and_the_search_still_runs` | E2E `SearchResultsTests` | the field turns amber, nothing says why |
| R20 | `The_box_s_filters_narrow_the_results_and_the_note_counts_what_they_hide` | E2E `SearchResultsTests` | no tab |
| R21 | `The_kebab_exports_exactly_the_results` (HTML and CSV downloads) | E2E `SearchResultsTests` | no tab |
| R22 | `Deep_results_join_after_the_instant_ones_of_their_strength_while_the_bar_runs` | E2E `DeepSearchTests` | the chip says "+N more found" |
| R23 | `Down_and_Up_on_the_Search_tab_scroll_and_stay_on_it` | E2E `SearchResultsTests` | no tab |
| R24 | `On_a_phone_the_type_switcher_and_Refine_replace_the_sidebar` | E2E `SearchResultsPhoneTests` | no tab |
| R25 | `Refine_is_a_bottom_sheet_that_Escape_closes` | E2E `SearchResultsPhoneTests` | no tab |
| R26 | `The_focus_goes_to_the_count_after_a_search_a_type_and_a_page_and_stays_on_sort_after_a_sort` | E2E `SearchResultsTests` | no tab |
| R27 | `An_old_q_link_opens_the_Features_tab_filtered` | E2E `UrlHashFilterHighlightTests` | guard |
| R28 | `A_Specifications_page_searches_as_TestRunReport_does` | E2E `SearchResultsTests` | no search on it |
| R29 | `On_a_phone_the_tab_row_scrolls_to_the_current_tab` (the Search tab after Enter, the History tab after Back) | E2E `SearchResultsPhoneTests` | no Search tab; History stays out of sight |

**Where it matched (S5)**

| # | Fact | Class | Red on 5.8.0 because |
|---|---|---|---|
| W1 | `data_search_is_byte_for_byte_5_8_0_s` (rich fixture) | unit `SearchDataAttributeTests` | guard |
| W2 | `data_search_kinds_says_where_each_part_starts` (scenarios and outline groups) | unit `SearchDataAttributeTests` | no attribute |
| W3 | `Display_text_folds_to_the_search_text_for_every_vector` | Jint `SearchIndexJintTests` | no function |
| W4 | `A_line_is_the_piece_s_line_cut_to_160_around_the_hit` | Jint `SearchSnippetTests` | no function |
| W5 | `The_worker_reports_the_kinds_a_match_hit` | Jint `SearchSnippetTests` | `matches` carries indices only |
| W6 | `The_worker_answers_snippets_for_the_items_asked_and_drops_stale_ones` | Jint `SearchSnippetTests` | no request |
| W7 | `A_payload_hit_shows_its_line_as_written_with_the_match_marked` | E2E `SearchHitTests` | no lines |
| W8 | `Opening_a_step_hit_opens_the_steps_and_flashes_the_step` | E2E `SearchHitTests` | no hits |
| W9 | `Opening_a_sub_step_hit_draws_the_sub_steps_first` | E2E `SearchHitTests` | no hits |
| W10 | `Opening_a_diagram_hit_draws_the_diagram_and_flashes_its_note` | E2E `SearchHitTests` | no hits |
| W11 | `A_hit_in_a_folded_note_opens_the_note` | E2E `SearchHitTests` | no hits |
| W12 | `A_hit_the_diagram_does_not_draw_says_so_and_offers_the_source` | E2E `SearchHitTests` | no hits |
| W13 | `Without_the_index_the_lines_come_from_the_page` (`FullSearchIndex` off) | E2E `SearchHitTests` | no lines |
| W14 | `The_worker_roster_holds_the_new_functions` | unit `NoteCopyFidelityScriptTests` | pin moves |

**The filters (S6)**

| # | Fact | Class | Red on 5.8.0 because |
|---|---|---|---|
| F-a | `The_box_is_hidden_when_the_page_opens` (markup and paint) | unit `FilterBarTests`, E2E `FilterBoxTests` | it shows |
| F-b | `Filters_shows_at_rest_when_pinned_and_on_a_phone` | E2E `FilterBoxTests` | hidden at rest and on a phone |
| F-c | `Filters_opens_the_box_at_the_bar_s_top_with_every_row_showing` | E2E `FilterBoxTests` | the rows are folded |
| F-d | `There_is_no_More_filters_and_no_phone_Filters_button` | unit `FilterBarTests` | both exist |
| F-e | `Filters_counts_the_filters_on_and_says_so_in_its_name` | E2E `FilterBoxTests` | More filters counts them |
| F-f | `Escape_and_the_close_button_close_the_box_and_focus_Filters` | E2E `FilterBoxTests` | at rest Escape does nothing |
| F-g | `The_box_stays_open_when_the_bar_comes_to_rest` | E2E `FilterBoxTests` | it is put back |
| F-h | `Opening_and_closing_keep_what_was_being_read_in_place` (at rest and pinned) | E2E `FilterBoxTests` | guard pinned, red at rest |
| F-i | `A_pinned_box_takes_at_most_60_percent_of_the_window_and_scrolls_inside` | E2E `FilterBoxTests` | guard |
| F-j | `A_page_with_only_its_features_has_the_bar_with_Features_and_Filters` | unit `ReportTabsTests`, E2E `FilterBoxTests` | no bar |
| F-k | `The_readout_is_announced_with_the_box_closed_and_open` (`role=status` on the bar's readout, never `display:none`, its text changes) | unit `FilterBarTests`, E2E `FilterBoxTests` | the live region is in the box |
| F-l | `A_link_with_filters_opens_with_the_box_closed_and_Filters_counting_them` | E2E `FilterBoxTests` | the box shows, folded |
| F-m | `The_rows_lay_out_by_measure_the_first_time_the_box_opens` (D18's `pair-fits`, `trio-fits`) | E2E `FilterBoxTests` | guard |
| F-n | `On_a_phone_Filters_and_the_readout_share_a_line_under_the_tabs` | E2E `MobileFilterLineTests` | the line is in the box |
| F-o | `The_box_s_exports_export_what_they_did` | E2E `FilterBoxTests` | guard |

**Sweeps, audits and budgets**

- `ViewportSweepTests` (320 to 1400, both themes) gains three states: the box open, the builder open, the Search tab
  with results; no overflow, no clipped control, nothing under the bar (`TOOLBAR_AT_EVERY_WIDTH_PLAN.md:1340-1341` left
  overlays out of the sweep; the builder is swept).
- `ControlTitlesAuditTests` sweeps the new controls (every one named and titled, D49).
- A contrast fact on the painted marks, pills and markers, both themes (4.11).
- `SearchOpenMs` and `SearchResultsMs` beside `DeepQueryMs` in `BrowserRenderWorkerTests` (4.12).

### 6.2 Facts and helpers that change

- **`FillSearchBar`** (`PlaywrightTestBase.cs:356-363`, 58 calls in 24 files) opens the builder with `/`, fills
  `#search-input`, presses Enter and, by default, goes back to the tab it was on, so a caller that reads the Features
  list keeps its meaning; a parameter keeps the Search tab. `OpenFilterRowsAsync` (115-127, 56 calls in 24 files) opens
  the box with Filters when it is hidden. The 47 direct `#searchbar` locators in 14 files move to the helper or to
  `#search-input`. CLAUDE.md's Playwright rule "**Search bar** — use `FillSearchBar()` …" (CLAUDE.md:66) is rewritten to
  say what the helper now does.
- **Rewritten**, because they pin what changes: `FilterBarTests` (E2E 15 facts; unit 54-67, 69-81, 91-104, 106-117,
  127-137), `MobileFilterLineTests` (9), `MobileResponsiveTests` (78-91, 109-123, 330-378, 540-567),
  `AdvancedSearchTests` (help panel 165-209), `DeepSearchTests` (the chip, 443-566), `ReportToolbarTests` (192-330),
  `ReportTabsTests` (74-86, 153-173), `ControlTitlesAuditTests` (346-354), `FocusClearOfTheBarTests` (426-466),
  `ViewportSweepTests` (239-249, 300-351), `RunBandLayoutTests` (45-61, 108-137), `ReportSectionWidthTests` (40-126),
  `ThemedPagesTests` (121-126, 151-198).
- **Touched** for the helpers only: `FilterPipelineTests`, `NotATestPaintTests`, `BackForwardRestoreTests`,
  `BackForwardFilterTests`, `BackForwardPlacesTests`, `FilterRowLabelTests`, `TagFilterTests`, `ScenarioInteractionTests`,
  `ToolbarStylingTests`, `UrlHashFilterHighlightTests`, `StableIdDeepLinkTests`, `ComponentsTabTests`, `TimelineTabTests`
  (line lists in `inventory/tests.md`).
- **Wiki media**: `WikiScreenshotTests` and `WikiGifTests` (the search GIF, `WikiGifTests.cs:680-711`) are regenerated.

### 6.3 Pins that move

| Pin | Why |
|---|---|
| `ReportThemeTests.cs:91-100` | the title line's exact markup |
| `FilterBarTests.cs` (unit) 54-137 | the box's seed, More filters, the phone toggle, the bar's markup and Filters' title, the close button, the page without tabs |
| `RunBandTests.cs:119`, `FilterRowsTests.cs:57-62`, 217-226 | slice from the `rows-folded` tag; the placeholder and the help rows |
| `StylesheetRulesTests.cs:210-234` | the fold rules, the phone toggle, `#searchbar`'s colours |
| `ControlTitlesTests.cs:337-395` | the field's, the help's, More filters', the phone toggle's and Filters' titles |
| `ReportTabsTests.cs:74-86, 153-173`, `FocusScriptTests.cs:73-83` | a page without tabs now has the bar |
| `FormStateRestorationTests.cs:87-99` | `autocomplete="off"` moves to the builder's input and stays on `#searchbar` |
| `KeyboardNavigationReportTests` (5) | `/` |
| `VerdictFencesTests.cs:37-89` | the SHA-256 of `advanced-search.js`, `report-search-function.js`, `report-search-index.js` (and of FMH if it changes) |
| `NoteCopyFidelityScriptTests.cs:74-89`, `VerdictSearchTests.cs:106-119` | the worker's roster |
| `AdvancedSearchReportTests`, `TagExpressionSearchReportTests` | literal strings of the page that move with the help |
| `.github/workflows/ci.yml:105-107`, 140 | the new classes join the "Search & Filters" lane and the Remainder lane's exclude list |

Unchanged, and checked to be: `SearchIndexReportTests` (the index's bytes), `CompressedPageTests.cs:305-319`, the
shared vectors, `PersistentFilterStateReportTests`, `UrlHashFilterTests` (UH changes only if 4.9's keys need it; they
are NAV's), `ToggleDefaultsBaselineTests` (no option changes).

### 6.4 Proofs

- **Red first.** Every new fact is copied into a worktree at `v5.8.0` (`29e551f3`) and run before any code is written;
  each red fact's failing line goes in `proofs/red-5.8.0.md`, and every guard must pass there. A fact added later gets
  its own red run (the checklist's lesson, "a fact added after the red run needs its own red run").
- **Mutations.** `checks/mutate-search.js` (node, as the ZOOM plan's), about 30, each counted only when its target
  passes unmutated (BACK :602-608): Enter running twice (B1, QB8); no `input` listener (B2); the box written without
  `hidden` (F-a); Filters hidden at rest (F-b); `rows-folded` seeded (F-c); the live region left in the box, or the bar's
readout given `display:none` while the box is open (F-k); the bar
  shutting the box at rest (F-g); the builder not stopping its keys (QB16); the draft written to `#searchbar` as it is
  typed (QB20); the tabs script's arrows not skipping the Search panel (R23); `-` not negating (L4); `feature:` ignored
  (L6); `status:` not a token (L2); the kinds offsets off by one (W2); pass 4 of the display text without its `i` (W3);
  the line cut one character short (W4); the kinds left out of `matches` (R4, W5); stale snippets kept (W6); Best match
  ignoring names (R13); `p=` not written (R14); focus not moved to the count (R26); the type counts from the instant pass
  only (R22); `/` preventing its default on an export (B7); `reset_filters` not closing what the search opened (B6); the
  empty path not unmarking rows (B5); the Exclude writing the plain form (R8); the Search tab left showing after Clear
  (R1); the hit opened without drawing the sub-steps (W9); a pinned box not capped (F-i); Filters' count from the box's
  readout instead of its controls (F-e). Each run, its result and any survivor's reason in `checks/mutations.md`.

### 6.5 Where they run

Chromium in CI, every class. Firefox and WebKit locally for the builder (the native dialog, composition, `inert`), R1
to R5, W7 to W10 and F-a to F-g, as 5.2.0's Back and Forward legs are (CI installs Chromium only). Widths 320, 390, 768,
769, 1012, 1161, 1280, 1400 and 1920; both themes; the Specifications page and its violet sheet; a compressed page; an
export; an `<iframe srcdoc>` page (BACK's S21 harness). The new E2E classes go in the "E2E (Search & Filters)" lane's
filter and the Remainder lane's exclude list in the same commit (`ci.yml:105-107`, 140). Playwright runs are announced
to live sessions first, and use the bundled browsers only (CLAUDE.md).

## 7. Release

### 7.1 One minor

The owner's word (A7), and it is what CLAUDE.md gives: **new public surface**, so the minor part moves. New controls (the
search button, its clear button, the builder, the Search tab, its type, sort, page, facet, Exclude, Advanced, kebab and
Refine controls); a new value of a documented address key and four new keys (`tab=search`, `type=`, `s=`, `o=`, `p=`,
as 5.2.0's `tab=` and `view=` moved the minor part); four new forms of the query (`tag:`, `status:`, `feature:`, `-`);
a new attribute on every scenario (`data-search-kinds`).

**The box hidden by default.** What a report shows when it opens changes: the box is gone from between the run band and
the bar until Filters is pressed, and its rows are always open when it shows. CLAUDE.md files under MAJOR "changing a
default so that existing code behaves differently without being touched"; no configuration option's default changes
here (nothing ever set the box's state: `ReportToggleDefaults.cs:15-137` has no such option), so this is a change to
generated report output, which CLAUDE.md does not make major on its own (its first judgement call) and records in the
Kronikol4J ledger. Precedents: 4.6.0 took sections out of the default report in a minor ("No existing option's default
changes … What a default report draws changes, which is a report output change", CHANGELOG.md:7570-7576), and 5.4.0
shipped a default the plan had called a major in a minor at the owner's word (CHANGELOG.md:822-826). The changelog says
what a script or test must now do: open the box with Filters (`.nav-filters-toggle`) before using a row's control, as
5.0.0 told them to press More filters (CHANGELOG.md:2000-2005); `#searchbar` is a hidden field holding the applied
query, the visible input being `#search-input` in the builder; and a search runs on Enter, no longer as it is typed.

### 7.2 Stages

All on one branch, released together; the number is taken when it is pushed.

| Stage | What | Facts | Gate |
|---|---|---|---|
| S0 | **Prototype on real reports**, in the harness only (`mock/`): the BreakfastProvider report (214 scenarios), an example project's report, its Specifications page and the 2,000-scenario report, each with the mock injected after its diagrams draw (the design memory's trap: a stylesheet injected right after load is lost). Screens of every state of Appendix B at 390, 768, 1012, 1280 and 1440, light and dark, Chromium and Firefox; the builder's width (4.3.1), Best match's weights (4.5.10) and the M2 count of `tag:`, `status:`, `feature:` in real text (4.4) settled on them | none | the owner looks at the screens and answers the questions they raise |
| S1 | The defects that live in code that stays: B3 to B6, B8, B9 | B3 to B8 | red on 5.8.0, green |
| S2 | The search button and the builder; Enter applies the query (the Search tab comes in S4) | T1 to T8, QB1 to QB22, B1, B2, B7 | red, green |
| S3 | The new forms and `searchQuerySpans` | L1 to L10 | red, green |
| S4 | The Search tab, with lines from the page | R1 to R29 | red, green |
| S5 | Where it matched: `data-search-kinds`, the worker's kinds and lines, display text, opening a hit | W1 to W14 | red, green |
| S6 | The filters, F1 to F9 | F-a to F-o | red, green |
| S7 | Sweeps, audits, budgets, mutations, the full core and Playwright suites, documentation (7.5), acceptance (7.6) | 6.1's last part | every suite green, every mutation killed or explained |

S6 does not wait for S2 to S5 and can be built first if the owner wants the filters sooner within the same release;
it touches FB, the box's and bar's markup and CSS, and none of the search's code.

### 7.3 Where

A worktree from `origin/main`, checked out LF (`git -c core.autocrlf=false -c core.eol=lf worktree add
C:/Code/Kronikol-search -b feat/github-search origin/main`), as every release since 3.29 (the parallel-sessions rules):
nothing is built in the shared checkout; Playwright runs and heavy builds are announced by message to live sessions;
`bin`, `obj` and `TestResults` are cleared once the release is pushed.

### 7.4 The sibling plan

`REPORT_UI_FIXES_PLAN.md` (kronikol-cf, written the same day in its worktree `C:/Code/Kronikol-uifix`, uncommitted; its
rules C1 to C13 in its section 4.1, its facts in 6.1) makes, in its I1, a tag or dependency chip two-way by default, with
a 2-WAY/3-WAY switch on each row for the three-state cycle; its I7 is phone CSS for the report-level YAML and WRAP selects
and the timeline's radios. This plan changes the box's container, its place and its visibility, not the chips. The lines
I1 changes, as kronikol-cf sent them, checked at `37428293` (5.9.0, which moved none of them):

| File | What I1 changes |
|---|---|
| RG | 2002-2027, the Tags and Dependencies rows (the switch right after each row's ANY/ALL); a `FilterWaysToggle` beside `FilterModeToggle` (3054-3056); the chips' titles, `ChipClicks` (3031), and a new `data-scenarios` on `FilterChip` (3027-3028); the comments at 1949-1953 and 3021-3026; the seeds `__TAG_WAYS_DEFAULT__` and `__DEP_WAYS_DEFAULT__` beside the mode seeds (1488-1502) |
| `report-filter-chips-script.js` | 7-41 (each row's ways beside `_tagMode` and `_depMode`); 115-184 (`cycle_chip`, the context-menu handler, a new `set_ways`, and Happy paths' Hide turning its row three-way) |
| UH | 130-272: new keys `tagstates=3` and `depstates=3`, written only while a row is three-way; `tagx`, `depx` and `hpx` imply three-way, so old links keep their meaning |
| EXP | 3-31: `reset_filters` puts both rows back to two-way |
| CSS | 522-546 (the switch and its pressed state, beside `.tag-mode-toggle`); 2202 (the phone's 36 px list of filter controls, which gains ANY/ALL and the row's Clear, its E1) |

Where the two meet:

- **`reset_filters`** (EXP:1-31) is changed by both: here for B6 (Clear All closes what the search opened), there to put
  the rows back to two-way. Different lines of one function; the second to ship keeps both and runs both plans' Clear
  All facts.
- **The address.** I1's `tagstates` and `depstates` and this plan's `type`, `s`, `o` and `p` (4.9) join today's `q`,
  `status`, `dur`, `pctl`, `hp`, `hpx`, `tags`, `tagx`, `cats`, `deps`, `depx`, `tagmode`, `depmode`, `tab` and `view`.
  No name is taken twice, and UH compares whole keys (`k === 'status'`), so `s` is never read as `status`. The wiki's
  address grammar (`Report-Configuration.md:285-293`, B9) gains both sets; whichever ships second writes its own.
- **F6's count** reads the chips' states (`tag-active`, `tag-exclude`, `dependency-active`, `dependency-exclude`,
  FB:63-65). I1 keeps those states, and its switch is a way of clicking, not a filter, so F6 does not count it: a
  three-way row with nothing chosen counts nothing.
- **The builder's value counts** (4.3.3) read the chips' counts (`TagChips.Count`, RG:2008, 2022), inside the rows I1
  rewrites; if I1 ships first, they read its `data-scenarios`.
- **The Search tab's facets** (4.5.3) write `tag:` and `-tag:` into the query. They are not chips, so I1's ways do not
  apply to them.
- **I1's E4** (`ViewportSweepTests` never opens the rows above 768 px) is met here: every row shows whenever the box
  does (F5), and the sweep gains the state "the box open" (6.1). The switch sits after ANY/ALL in each row either way,
  and kronikol-cf confirms nothing else changes on its side.
- **CSS.** This plan's blocks (249-293, 857-929, 1981, 2050-2056, 2149) do not overlap I1's (522-546, 2202).

Whichever releases second rebases on the other and runs the other's facts on the merged code (I1's section 7.1 says the
same).

### 7.5 Documentation

- **Wiki** (lines at `1f7944e`; a wiki worktree of the session's own, made from a private ref, edited at release and
  committed at once):
  - `Search-Syntax.md`: the opening tip (:1-7, `/` opens the search, "Search syntax tips" replaces `?`); the new forms in
    the qualifiers section (:157-213); instant and deep search (:247-310), with B3's correction (:11) and the gap the hit
    lines close (:297-299, "pinpointing which note, row or diagram matched is not highlighted"); a new section, "The
    search and its results", for the builder and the Search tab.
  - `Generated-Reports.md`: the title line (:94-95); the filter box (:98-99, 717-732); the report bar and Filters
    (:673-688); the phone (:692-703); search and filters (:705-716); the link grammar (:759-769, 1186-1214); keys and
    tooltips (:963-990); the exports (:994-1003); Back and Forward (:1216-1245).
  - `Report-Configuration.md`: the address grammar (:285-293, B9) and `q=`'s neighbours.
  - `Migrating-to-v5-Report.md` (:16, 22, 68-70: tests press Filters, not More filters; `#search-input`), `Migrating-to-v5.md`
    (:17, 90), `Report-Themes.md` (:29-30), `PlantUML-Browser-Rendering.md` (:169, 261), and the pages that say "search
    bar" (`Tags-and-Attributes.md:22, 68`, `Marking-New-and-Changed-Tests.md:3, 16`, the four integration pages, `Cross-Run-
    History.md:17, 630, 642, 901`, `FAQ.md:109`, `Home.md:63`, `Querying-Reports.md:1028`, `Step-Tracking.md:267`).
    `tools/wiki-links/wikilinks.py` checks the result.
- **README**: line 24 ("Search everything") gains the results that show where each match is.
- **CHANGELOG**: a draft is in `drafts/changelog.md`, saying which part moved and why (7.1).
- **CLAUDE.md**: the Playwright rule on the search bar (6.2).
- **Kronikol4J**: a ledger entry in `docs/REMAINING_PARITY.md` before "Explicitly OUT OF SCOPE", in the form of the 5.5.4
  entry (draft in `drafts/kronikol4j-ledger.md`): not mirrored, the port's page is 3.0.43's; byte parity breaks on every
  page; the data files are unchanged (`data-search-kinds` is in the HTML only).
- **`PLANS_STATUS.md`**: this plan's row, updated per stage.

### 7.6 Acceptance

- BreakfastProvider built on local packages of the release with nothing configured (the consumer recipe), its report
  opened in Chromium, Firefox and WebKit, light and dark, at 390, 1012 and 1440: every surface of Appendix B, a search
  that only deep search answers, a hit opened in a payload, Back, the box opened at rest and pinned.
- The 2,000-scenario report: 4.12's budgets.
- A Specifications page with no durations (F7), a compressed page, an export (`/` and the scripts idle), an
  `<iframe srcdoc>` page.

### 7.7 Steps

Per CLAUDE.md and the releases before it: tests first; the core and full Playwright suites green; `release.slnf` built in
Release; the version moved in every package (`Directory.Build.props:4`, `.claude-plugin/plugin.json`,
`.claude-plugin/marketplace.json`, `templates/github-actions/kronikol-history/VERSION`; the templates' pins only once
nuget.org lists every id); the changelog entry, with `[Unreleased]` folded in if there is one; "taking X now" to live
sessions; commit, tag `v{version}`, push both, with no "fixes #N" and no CI skip marker quoted in the message; CI,
CodeQL and Summary Preview checked green; then the wiki, the Kronikol4J ledger line, this plan's row and the memory.

## 8. Questions for the owner

Recommended answers first; work proceeds on each recommendation unless the owner says otherwise.

| # | Question | (a) recommended | (b) | Why (a) |
|---|---|---|---|---|
| Q1 | **What does the query narrow?** | the whole report, as today: the Features list, the timeline, the components, the exports and the Search tab | only the Search tab: the query stops being a filter; `q=` links open the Search tab; the exports, the timeline and the components lose their text filter; 24 test files change meaning | 4.1: nothing a reader or a link relies on stops working, and GitHub's page has nothing beside it to narrow |
| Q2 | **When is the Search tab there?** | while a query is applied | always, empty until a search | GitHub's results page exists only once you search; an empty tab is a tab to explain |
| Q3 | **When does a search run?** | on Enter, as GitHub's | as it is typed, as today (the builder's search row would show the live count) | the dialog covers the page while typing, so a live result is not seen; one Enter is one entry |
| Q4 | **A second key, `S`, as GitHub has?** | no, `/` only | `/` and `S` | a single-letter shortcut needs a way to turn it off (WCAG 2.1.4); the report has no settings and stores nothing |
| Q5 | **Recent searches in the builder?** | yes, this page's, in memory only | none | GitHub's builder shows them; memory only keeps the theme-only storage rule |
| Q6 | **Results a page?** | ten, GitHub's | 25 | the page count is GitHub's; jump to and the Features list cover the long lists |
| Q7 | **The phone's sheet button?** | "Refine" | "Filter", GitHub's word | the bar's "Filters" is the report's filter box, one tap away |
| Q8 | **GitHub's `AND`/`OR`/`NOT` words and `/regex/`?** | neither | capital `AND`, `OR`, `NOT` as operators (the query's case kept to the tokenizer); regex read without the index | 4.4: the words are everyday step text; regex reads every payload |
| Q9 | **Counts on the facets?** | yes, muted | names only, as GitHub | the report knows them at no cost and its chips already carry them |
| Q10 | **The bar on a page with only its features?** | yes, with the one tab | no bar: Filters in the Features tools, and the Search tab nowhere | F7: Filters and the Search tab need the bar, and one rule for every page is simpler than two |
| Q11 | **A scenario skipped after a failure, for `status:`?** | failed, as the Status buttons count it | its own name only, as `$status` today; or skipped, as the run band counts it | B4: the search and the filter it stands beside must agree |
| Q12 | **S0, the prototype, before building?** | yes | build straight from the plan | the 5.0.0 redesign's rounds, and the design memory: screens on the real report settle what a plan cannot |
| Q13 | **A ROADMAP row?** | none, as feature plans since 5.0.0 | a stage-1 row | precedent (LONG_NOTE_SCROLL_PLAN, HTML_COMPRESSION_PLAN Q12) |
| Q14 | **Kronikol4J?** | the ledger entry only | port it | the port's page is 3.0.43's |

## 9. Log

- **2026-10-26, written** (kronikol-41) at the owner's ask, as a plan only. Four read-only research passes: the search
  today (section 2.2, `inventory/search.md`), the title line, box, bar and tokens (2.1, 2.3, `inventory/page.md`), the plan
  conventions, decisions and documentation (`inventory/context.md`), and GitHub's search, live and from archived copies
  (section 3, `github-ref/`). The live BreakfastProvider report measured (M1). Defects B1 to B9 read in the code; a
  suspected tenth (the bar's refit, FB:99-107) probed and not a defect. kronikol-cf, writing `REPORT_UI_FIXES_PLAN.md`
  the same day, told of the overlap; it names this plan as a sibling, and sent the exact lines its I1 changes, checked at
  5.9.0 (`37428293`), now in 7.4 with where the two plans meet. Nothing built, nothing committed.

## Appendix A. The harness

`SEARCH_PLAN.harness/`:

| Path | What |
|---|---|
| `github-ref/` | GitHub's pages measured: `measurements.json` and `tokens-from-github-css.json`, the probe scripts (`lib.js`, `probe1` to `probe10`, `compile-measurements.js`), the 94 screenshots they write (live and archived, named by state and width; not committed, the probes write them again), and `README.md` (the sources cited in section 3, a summary of the archived `qbsearch-input` markup, and the design decisions as the research listed them). GitHub's own stylesheets, scripts and page HTML are not kept: the probes fetch them again |
| `current/` | the live report measured: `probe.js` and `probe.json` (M1), `probe-fit.js` (section 5's check), and the screenshots `probe.js` writes at 390 to 1440, light and dark (not committed) |
| `inventory/` | the research notes behind sections 2 and 4, with their line maps: `search.md`, `page.md`, `context.md`, and `tests.md` (every test file and line 6.2 touches) |
| `drafts/` | `changelog.md`, `kronikol4j-ledger.md` |
| `mock/`, `proofs/`, `checks/` | `mock/` made in S0, `proofs/` from S1 on, `checks/` in S7 |

Probes launch the bundled Chromium only (`playwright-core` with no `channel`), never an installed Chrome or Edge (CLAUDE.md).

## Appendix B. Wireframes

At 1280, light. The title, the totals (214 scenarios, 73 features) and the tab names are the measured report's;
the other names and counts are made up to show each state. Sizes in CSS pixels. Key: `(o)` the magnifier, `(x)` a
clear button, `[ C ]` the theme switch, `[/]` a key cap, `[failed]` a value on the selected tint, `**word**` a match
at 700, `==word==` a match in a `<mark>`, `||` at a row's left the 4 px accent bar, `v` and `x` a passed and a failed
glyph, `#` a feature, `(*)` a status dot, `(+)` a plus-circle row, `[...]` the kebab.

**B.1 The title line, nothing searched**

```
 Breakfast Provider (ReqNRoll) Test Run Report             [ (o)  Type [/] to search           ] [ C ]
 h1 22 px, weight 600                                      search box 384 x 34, 8 px corners   gap  switch
                                                                                               8 px 34 x 34
```

**B.2 The title line, a query applied; and at 390**

```
 Breakfast Provider (ReqNRoll) Test Run Report             [ (o)  pancake status:[failed]  | (x) ] [ C ]

 Breakfast Provider            [(o)] [ C ]   390 px: two 34 x 34 buttons; (o) takes the selected look
 (ReqNRoll) Test Run Report                  while a query is applied, and its clear is in the builder
```

**B.3 The builder, text typed** (its input over the search box, the page dimmed behind)

```
            +--------------------------------------------------------------------------------+
            | [ (o) pancake                                                            (x) ] |
            |                                                                                |
           || (o) pancake                                                             Search |   <- the active row: the hover
            |  (o) feature:"Pancakes Creation" pancake                Search in this feature |      ground and the 4 px bar
            |  JUMP TO                                                                       |
            |  v  A valid **pan**cake request returns a batch   Pancakes Creation    Jump to |
            |  x  **Pan**cakes over the daily limit are refused   Pancakes Creation  Jump to |
            |  #  **Pan**cakes Creation                                              Jump to |
            |--------------------------------------------------------------------------------|
            |  Search syntax tips                                                            |
            +--------------------------------------------------------------------------------+
```

The jump rows mark the letters typed at 600 (here only "pan" shown, for width).

**B.4 The builder, a key's values**

```
            +--------------------------------------------------------------------------------+
            | [ (o) pancake status:                                                    (x) ] |
            |                                                                                |
            |  STATUSES                                                                      |
            |  (*) passed      208                                              Autocomplete |
            |  (*) failed        3                                              Autocomplete |
            |  (*) skipped       2                                              Autocomplete |
            |  (*) bypassed      1                                              Autocomplete |
            |--------------------------------------------------------------------------------|
            |  Search syntax tips                                                            |
            +--------------------------------------------------------------------------------+
```

**B.5 The Search tab**

```
 [ Features | Timeline | Components | History | (o) Search 12 ]       Filtered 12 of 214  Clear All  [ Filters ]
 +-- FILTER BY ---------------+   12 results (85 ms)                           SORT [ Best match v ]  [...]
 ||Scenarios             [12] |  +--------------------------------------------------------------------------+
 | Features               [4] |  | v Pancakes Creation / A valid **pancake** request returns          1.2 s |
 | Steps                  [9] |  |   [smoke] [api] · Happy path                                             |
 | Diagrams               [7] |  |  +--------------------------------------------------------------------+  |
 | Errors                 [1] |  |  | STEP     Given a valid **pancake** request                         |  |
 |----------------------------|  |  | DIAGRAM  POST /api/==pancake==s {"flour": 200, ...                 |  |
 | STATUS                     |  |  | DIAGRAM  {"id": 42, "type": "==pancake==", ...                     |  |
 | (*) Passed              11 |  |  | - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -|  |
 | (*) Failed               1 |  |  | Show 4 more matches                                                |  |
 | TAGS                       |  |  +--------------------------------------------------------------------+  |
 | smoke                    8 |  +--------------------------------------------------------------------------+
 | api                      5 |    ... nine more results ...
 | (+) More tags...           |
 | DEPENDENCIES               |                        < Previous  [1]  2  Next >
 | Payment Service          3 |
 | ADVANCED                   |   Press / to search again and change the query.
 | (+) Feature                |
 | (+) Tag                    |
 | (+) ...                    |
 | Search syntax tips         |
 +----------------------------+
   296 px, sticks under the bar    fluid, 24 px from the sidebar
```

**B.6 The Search tab at 390**

```
 [ …onents | History | (o) Search 12 ]   <- the row scrolled to the current tab (4.5.9)
 [ Filters ]  Filtered 12 of 214  Clear All
 [ Scenarios 12 v ]  [ Refine ]  [...]
 12 results (85 ms)
 +----------------------------------------+
 | v A valid **pancake** request    1.2 s |
 |   Pancakes Creation                    |
 |   STEP                                 |
 |   Given a valid **pancake** request    |
 +----------------------------------------+
```

**B.7 The filters**

```
 At rest, closed:
 [ Features 73 | Timeline | Components | History ]          All 214 scenarios, 73 features  [ Filters ]

 Open (two filters on), at rest or pinned:
 +-- FILTERING ---------------------------------------------------------------------------------------+
 |         Filtered 12 of 214 scenarios · Clear All · Export Filtered HTML · Export Filtered CSV  (x) |
 | STATUS  [Passed] Failed Skipped Bypassed   DURATION >= P50 P90 P95 P99 Custom   HAPPY PATHS ...    |
 | TAGS    ANY  [@smoke 12]  @api 40  ...                                                             |
 | DEPENDENCIES  ANY  Payment Service 30  ...                                                         |
 +----------------------------------------------------------------------------------------------------+
 [ Features 73 | Timeline | Components | History ]                                       [ Filters (2) ]
   the bar's readout and Clear All give way to the box's header (4.7, F6); pinned, the box takes at most
   60% of the window and scrolls inside

 Phone, closed:
 [ Features 73 | Timeline | Compo… ]
 [ Filters (2) ]  Filtered 12 of 214  Clear All     <- its own line under the tabs
```
