# GitHub's search, measured (2026-10-26)

The reference behind SEARCH_PLAN.md section 3. Every probe here launches the bundled Chromium through
`playwright-core` with no `channel` (CLAUDE.md: never an installed Chrome or Edge) and writes next to itself
(`__dirname`). `lib.js` holds the shared helpers; `probe4` to `probe10` measure the live header, the quick
search, the results pages and the archived query builder; `compile-measurements.js` gathers their output into
`measurements.json`. GitHub's stylesheets, scripts and page HTML are not kept here; the probes fetch them again.
Screenshots are named by state and width (`qb-typed-lan-1280-light.png`); `wayback-<date>-` marks a render of an
archived copy, with every live GitHub host blocked.

What follows is the research summary: sources, numbers, and the decisions as listed.

Measured = bundled Chromium 149 (Playwright 1.61), headless, scale 1, logged out. CSS = GitHub production sheets fetched
2026-10-26: G global-58d1122073b19732.css, P primer-38ae573fa780d76a.css, PR primer-react-css.dc074e1b2708fdd8.module.css,
B blackbird-search.27cfa19c462af0ec.module.css; JS blackbird-search-2f99523020e26693.js. ~20 github.com loads.
KEY FINDING: logged-out github.com today has NO qbsearch-input: a magnifier button in a black marketing header opens a small
React "Quick search" dialog with NO qualifier suggestions. The Catalyst <qbsearch-input>/<query-builder> (logged-in component)
measured from Wayback copies (2025-05-28, 2026-03-01, 2025-06-28) with live GitHub hosts blocked = "archived".

## A. Header entry point
- Logged in (docs/changelog/CSS G): 2023-10 order: hamburger, logo, breadcrumbs, search box "Type [/] to search" with ">_"
  command palette button at right end, divider, "+ v", Issues, Pull requests, Inbox, avatar
  (https://github.blog/changelog/2023-10-17-redesigned-navigation-now-available-to-all-users/; image
  https://github.com/primer/design/assets/3369400/3a319003-b6a5-47d0-ad35-01015f61a74e). 2025+: Copilot + Agents buttons between
  search box and divider (https://github.blog/changelog/2025-08-19-agents-panel-launch-copilot-coding-agent-tasks-anywhere-on-github-com).
  2026-05: ">_" gone (https://github.blog/changelog/2026-05-18-ask-questions-in-context-with-copilot-on-web/).
- CSS G sizes: bar 16px padding, 12px gap, 32px controls (64px tall), bg --bgColor-inset (#f6f8fa / #010409), 1px bottom
  border. Search box min 12rem max 24rem (192-384px), right-aligned; button 32px tall, 1px --button-default-borderColor-rest
  border, padding 5px 31px, transparent bg; placeholder --fgColor-muted, 20px line height; leading magnifier 16px at 8px
  top/left; key hint 16x16, 12px text, 1px border --fgColor-muted, radius 3px; trailing action 24x24 after a 1px x 16px
  divider; icon buttons 32x32 radius 6; unread dot 8px. <=1011.98px box -> icon button; opened it covers whole bar
  (absolute top 0 left 0 width 100%). <=767.98px action buttons + Copilot group hidden.
- Overlay grows from the box: `.search-suggestions {top:-14px; left:-14px; width:calc(100% + 26px); padding-top:12px;
  border-radius:12px; max-height:80vh; z-index:35}`; `.dark-backdrop` --overlay-backdrop-bgColor.
- Hotkeys: "S or / | Focus the search bar." (https://docs.github.com/en/get-started/accessibility/keyboard-shortcuts);
  measured both open search.
- Logged out (measured): 1440: header 72px #000; trigger 200x32 radius 6, 1px #262c28 border, label "Search" 14px/500 #fff,
  magnifier hidden, kbd "/" 24.3x20 11px Mona Sans Mono, 1px #353d37, radius 4, padding 4px 8px, inset 0 -1px #191f1b.
  1280: 42x32 magnifier only at x1003; Sign in 71x32, Sign up 76x32, Appearance 32x32; 8px gaps. 1012: 42x32. 768/390:
  inside hamburger panel as full-width 32px box "Search" + "/". Breakpoints: icon-only 63.25rem-87.499rem; 200px slot from
  87.5rem (1400px); 100% inside menu below 63.25rem. ARIA: aria-label="Search or jump to, type / to search",
  aria-haspopup="dialog", aria-expanded, <kbd aria-hidden="true">. Archived Catalyst button: aria-label="Search or jump to…",
  placeholder="Search or jump to...", data-hotkey=s,/. Archived logged-out headers also icon-only 1012-1280 (30x30 in a 32px
  box 1px #818b98 on #25292e); wide "Search or jump to..." box 38px tall only inside the <=1011px menu.
- Markup: archive/qbsearch-input-2026-03-01.html (qbsearch-input data-scope="repo:..." data-custom-scopes-path
  data-max-custom-scopes="10" data-blackbird-suggestions-path data-jump-to-suggestions-path data-current-repository
  data-current-org data-logged-in; button.header-search-button aria-label="Search or jump to…" aria-haspopup="dialog"
  data-hotkey=s,/; span "Search or jump to..."; slash keycap svg 22x20; modal-dialog role=dialog aria-modal=true
  aria-labelledby (sr-only h1 "Search code, repositories, users, issues, pull requests..."); .search-suggestions
  position-fixed radius 12px; query-builder: FormControl, sr-only label "Search", QueryBuilder-StyledInput (leading search
  octicon), StyledInputContent aria-hidden mirror, Sizer, input type=text role=combobox spellcheck=false aria-expanded
  autocomplete=off; clear button (x-circle-fill, sr "Clear"); ul role=listbox class ActionListWrap QueryBuilder-ListWrap
  aria-label="Suggestions" tabindex=-1; FormControl-inlineValidation (alert icon) hidden; sr-only aria-live=polite
  aria-atomic=true feedback div; footer .search-feedback-prompt link "Search syntax tips" (docs URL, target _blank);
  feedback dialog; custom-scopes dialog "Saved searches" (Name <=50 placeholder github-ruby; Query <=500 placeholder
  "(repo:mona/a OR repo:mona/b) AND lang:python"; Cancel | Create saved search)).

## B1. Live logged-out "Quick search" (Primer React Dialog), measured
- 1440 trigger 1005,20 200x32 -> dialog 740,7 480x272.5; 1280 trigger 1003,20 42x32 -> 580,7 480x272.5; 1012 -> 312,7
  480x272.5 (right edge = trigger right + 15; top = trigger top - 13; input lands on trigger); 768 -> 0,0 768x272.5 radius
  0 0 12 12 top sheet; 390 -> 0,0 390x272.5.
- CSS: --qs-anchor-top/left/width; --qs-align-offset-x:-9px; --qs-align-inset-right:15px; --qs-align-offset-y:-13px; from
  1012 width max(anchor + 24px, 480px); <=1011 top sheet inset 0 0 auto, no top radius, max-height 80dvh. Text typed +
  nothing to suggest: 108.5px tall.
- Dialog: role=dialog aria-modal=true, sr h1 "Quick search", focus trap sentinels; radius 12; bg #fff / #010409; shadow
  light `0 0 0 1px #d1d9e040, 0 6px 12px -3px #25292e0a, 0 6px 18px #25292e1f`, dark `0 0 0 1px #3d444d, 0 6px 12px -3px
  #01040966, 0 6px 18px #01040966`; backdrop fixed #c8d1da66 / #21283066 fade 0.2s; panel 0.2s cubic-bezier(0.3,0.8,0.6,1)
  opacity 0->1 + translateY(-8px)->0; off under reduced motion.
- Input row: container padding 12px; input 456x32 (at 480 wide), radius 6; focused 1px #0969da border + 2px #0969da outline
  (#1f6feb dark); bg #fff (#0d1117 dark); inset shadow 0 1px 0 #1f23280a; magnifier 16px #59636e + 8px gap; text 14px/20px;
  input text transparent, caret --fgColor-default; placeholder "Search or jump to..." #59636e (#9198a1 dark); clear button
  (x-circle-fill, tooltip "Clear search") once text.
- Styled overlay [data-testid=quick-search-input-overlay] aria-hidden, absolute, 33px from left, 41px from right,
  white-space:pre, pointer-events:none. Key plain #1f2328 (key without value not styled); value (filterValue) #0969da on
  #ddf4ff radius 3 (dark #4493f8 on #388bfd1a); NOT/OR/leading "-" (entity) #6639ba; /regex/ (constant) #0969da; quotes,
  parens plain.
- List: ActionList role=listbox aria-label="Search suggestions", 8px bottom padding; group heading "Explore" 12px/600
  #59636e (#9198a1), inset 16px; options are <a role=option aria-selected>, 33.5px tall, li margin 0 8px, radius 6, padding
  6px 8px; 16px icon #59636e + 8px; label 14px #1f2328; trailing "Jump to" 12px #59636e on EVERY row; no Enter glyph.
- Active option: bg #818b9826; 4px bar 8px outside left edge, top 4px, height 100% - 8px, radius 6, #0969da (#1f6feb dark)
  (Primer [data-is-active-descendant]::after in PR); label looks semibold.
- Empty input: Explore -> GitHub Actions, Blog, CI/CD, Code review. Typing anything: list empties, aria-expanded false.
- Keyboard: nothing active on open; Down activates first, focus stays in input; Up on first does not wrap; Home/End don't
  move active; Esc closes + focus to trigger; Tab -> option with tabindex 0 -> "Search syntax tips" -> input; Enter
  submitted /search?q=playwright+is%3Aopen&type=repositories (current type not kept).
- Footer 44.5px, padding 12px 16px, 1px top border #d1d9e0b3; link "Search syntax tips" 12px #0969da.

## B2. Archived Catalyst query builder (logged-in component), measured
- On a repo page input prefilled with scope "repo:microsoft/playwright " (data-scope).
- Container x=80 y=6 1120 wide, radius 12, 1px #d1d9e0 border, shadow `0 0 0 1px #d1d9e0, 0 40px 80px #25292e3d`; dark bg
  #0d1117 border #3d444d shadow `0 0 0 1px #3d444d, 0 24px 48px #010409`; 12px top padding; backdrop #c8d1da66 / #21283066.
- Styled input 1094x32 with 12px side margins; items 34.75px tall, margin 0 8, radius 6, grid icon / label / trailing text;
  trailing text 14px (CSS G).
- Item: <li role="option" class="ActionListItem" data-type="query-result" data-value aria-label data-replace-query-with
  data-move-caret-to id="query-builder-test-result-0">.
- Typed: "lan" -> 3 rows "lan · Search all of GitHub", "repo:microsoft/playwright lan · Search in this repository",
  "org:microsoft lan · Search in this organization". "language:" -> "Languages" group colour dots C++, Go, Java,
  JavaScript, PHP, Python, Ruby, trailing "Autocomplete". "language:py" -> Python, Papyrus, Ren'Py, Jupyter Notebook
  (fuzzy). "is:" -> "Values" group: public, private, issue, pr. "repo:" -> only "Search all of GitHub". "-is:" -> fork.
  "foo (bar" and "/unclosed": inline validation stayed hidden.
- Live region "1 suggestion." / "3 suggestions." / "7 suggestions." (aria-live=polite aria-atomic=true).
- Tokens: qb-filter-value #0969da on #ddf4ff radius 3; qb-entity #6639ba (#d2a8ff); qb-constant #0550ae (#79c0ff).
- Active item bg #818b9826 + 4x24px bar at left -4px (.ActionListItem[aria-selected=true]:after in P).
- Technique (CSS G): `.QueryBuilder-Input {color:#0000; caret-color:var(--fgColor-default)}`; `query-builder:not(:defined)
  .QueryBuilder-Input {color: fgColor-default}`; `.QueryBuilder-ListWrap {max-height:20em}` (60vh in search).
- Logged in (docs/blog): empty box shows recent searches, suggested repos/teams/projects, saved searches; typing shows
  completions + jump to files (https://docs.github.com/en/search-github/github-code-search/using-github-code-search).
  Saved searches: saved:, "Manage saved searches", "Create saved search". 2023 release screenshot: "Clear" inside input, "x"
  close, footer "Search syntax tips" + "Beta" + "Give feedback"
  (https://github.blog/news-insights/product-news/github-code-search-is-generally-available/). Rationale:
  https://github.blog/engineering/user-experience/creating-an-accessible-search-experience-with-the-querybuilder-component/

## C. Results page (live, measured)
- URL q, type, s+o ("Newest" -> s=created&o=desc), p (Next -> p=2); facet links put qualifier in q; code clears l and p.
  10 results per page; pagination "1…7 … 100"; payload page_count 100, elapsed_millis, result_count, warn_limited_results,
  errors[]. No GitHub doc defines s/o/p/l.
- Layout: 1440 sidebar 296+1, padding 16 24, main 731, aside 340. 1280 sidebar 296+1 (#d1d9e0 / #3d444d), main x321 571,
  aside x916 340 (margin-left 24); card 571x163 padding 16, 1px border, radius 6, 16px gap. 1012 sidebar 296, main 383,
  aside 260. 768 sidebar 257, main 462, aside hidden. 390 sidebar hidden, padding 16, main 358. No max-width up to 1440
  (fluid). Sidebar = Primer page-layout pane, scrolls by itself, drag handle (role=slider "Draggable pane splitter").
  Geometry identical in dark.
- Sidebar (1280): "Filter by" h2 16px/600 padding 16 16 8. Types (Code, Repositories, Issues, Pull requests, Discussions,
  Users, then "More" -> Commits, Packages, Wikis, Topics, Marketplace): row 32px, li margin 0 8, radius 6, padding 6px 8px,
  16px icon #59636e, label 14px. Current: bg #818b9826 (#656c7633), label 600, 4x24 bar at left -8px radius 6 #0969da
  (#1f6feb). Hover #818b981a. Counters 20px tall 12px/600 padding 0 8 radius 20 bg #818b981f (#656c7633); abbreviated
  ("171k", "3.9M"); "..." while loading; sr " (171k) results". Dividers 1px #d1d9e0b3 8px margins. Group headings 12px/600
  #59636e. Language rows 32px, 10px dot (radius 8, 1px translucent border, inline colour), then "More languages…".
  "Advanced" rows with plus-circle icon: most append a qualifier key to the header search input, which opens with caret at
  end; some navigate (JS). Logged out clicking did nothing; 2025-06-28 archive: Owner -> "react owner:" caret at 12.
  Repos: Owner, Size, followers, forks, stars, created, pushed, topic, license, archived; Public/Private navigate. Issues:
  Owner, State, Close reason, Has linked PR, Author, Assignee, Mentioned user/team, Commenter, Involved user, Label,
  Milestone, Number of comments, Number of interactions; "State" group (Open, Closed). Users: Organization, Full name,
  repositories, followers, Location, Language, Creation date, Sponsorable. Facet rows show "Exclude {name}" x on
  hover/focus -> adds NOT … (JS; .FacetOption). "Advanced search" link ends list.
- Results header: "171k results" 16px/500 + "(154 ms)" 12px/400 #59636e; seconds from 1000ms; "About "/"More than "
  prefixes. Hidden h2#search-results-count tabindex=-1 data-react-autofocus gets focus after search. "Sort by: Best match"
  button 139.8x28, bg #f6f8fa, 1px #d1d9e0, radius 6, 12px/500, shadow 0 1px 0 #1f23280a; menu role=menu 256px wide, 32px
  menuitemradio rows, check on current. Sort options: Repos: Best match, Most stars, Fewest stars, Most forks, Fewest forks,
  Recently updated, Least recently updated; Issues: Best match, Most/Least commented, Newest, Oldest, Recently updated, Least
  recently updated; Users: Best match, Most/Fewest followers, Most/Least recently joined, Most/Fewest repositories. "Open
  column options" 28x28 kebab: View search docs, Advanced search. "Save" only logged in.
- Cards: Repo: 20px avatar radius 4; title link 16px/500 #0969da (#4493f8), underline on hover; matches <em> 700 in titles,
  600 in descriptions, no background; description 14px/21px; topic pills 12px/500 #0969da on #ddf4ff (#388bfd1a) line
  22px padding 0 10px radius 2em 8px gaps; meta 12px #59636e: 10px language dot, star count, "Updated …", "·" 8px margins;
  Star button 28px. Issue: repo name 12px/500 #59636e; open icon #1a7f37 (#3fb950); title 16px bold matches; body snippet;
  neutral labels 12px/500 1px #d1d9e0 pill padding 0 6 20px; meta 16px avatar, comments, "Opened …", "#41". User: 40px
  avatar; name 16px/500 + login 16px/300; Follow. Code search needs sign in; CSS B: matches <mark> --highlight-neutral-bgColor
  (#fff8c5 / #d2992266); "Show N more match(es)" / "Show less" 40px tall 12px muted dashed top border.
- Pagination: 32x32 radius 6 14px 4px gap; current white on #0969da (#1f6feb) aria-current=page; Previous disabled #818b98;
  Next #0969da; labels "Page 2", "Next Page".
- Loading: 3px #0076ff progress bar at top; type marker moves immediately; facets + results become #818b981f skeleton
  blocks with 1.5s "shine" animation; no aria-busy, no live region.
- Empty state: bordered box 935 wide, 217x135 illustration; "Your search did not match any repositories" 20px/600; "You
  could try one of the tips below." 14px muted; accordion tips "Search across an organization", "Saved searches" (41px,
  14px/500); "You could try an advanced search." 12px. Other types matched: "However we found 8.1M pull requests and 1.9M
  issues that matched your search query. Alternatively try one of the tips below."
- Invalid queries: repo search -> no error, 0 results; code search shows banner e.g. "Invalid repository name: asdfghjkl"
  (https://github.blog/engineering/user-experience/accessibility-considerations-behind-code-search-and-code-view/).
- Narrow (<768): sidebar hidden, Sort hidden; row "Filter:" + type switcher (menu of every type with count), "Filter"
  button, kebab; "Filter" opens bottom-sheet dialog "Filters" radius 12 12 0 0, slides up 0.25s, Languages + Advanced.
- Header query: logged out 2026 not shown; archived results page (2025-06-28) box expanded to 320x32 showing the query
  from data-initial-value; reopening builder kept the query.

## D. Syntax
- Code search docs: whitespace AND; AND/OR/NOT keywords; parens; no documented precedence; quotes exact (\" and \\ escapes);
  repo:, org:, user:, language:, path: (globs), symbol:, content:, is:archived|fork|vendored|generated; /regex/;
  case-insensitive default, (?-i); unparseable = literal text. Limits 1000 chars, 100 results, no sorting.
- Other search: >, >=, <, <=, n..n, n..*, *..n; ISO dates; -qualifier exclude; NOT on words only; quote values with
  spaces; @me. Limits 256 chars, <=5 AND/OR/NOT.
- Highlighting: aria-hidden div of spans behind native input with transparent text; contenteditable rejected. Key color
  fg.default; value color fg.accent bg bg.accent.

## E. Rationale + Primer
- 2021 preview: stay in flow, org:/repo: autocomplete (https://github.blog/news-insights/product-news/improving-github-code-search/).
- Ranking matters; p95 under a second (https://github.blog/engineering/architecture-optimization/a-brief-history-of-code-search-at-github/).
- Query time shown so people iterate quickly (https://github.blog/developer-skills/github/a-better-way-to-search-navigate-and-understand-code-on-github).
- QueryBuilder post 2022: combobox; group headers role=presentation (role=group in listbox poorly supported), type name at
  end of each option's aria-label; navigating rows say "…, jump to this repository"; visible "Jump to"/"Autocomplete" tells
  sighted users what Enter does; polite live region announces suggestion count + clearing, toggling a no-break space so a
  repeated count is re-read; announcing caret position tested and dropped.
- After a search, focus goes to the heading with the result count (not first result, not aria-live); focus returns to
  Sort after re-sorting (2023 accessibility post).
- 2023 nav: breadcrumbs, issues/PRs one click, search + command palette, Primer
  (https://github.blog/news-insights/product-news/exploring-github-with-the-redesigned-navigation-now-in-public-beta/).
- Primer as rendered: ActionList item 32px (33.5 dialog), padding 6px 8px, radius 6, li margin 0 8, 14px label, 16px icon +
  8px; NavList current bg #818b9826/#656c7633, label 600, ::after bar 4px at -8px, height 100%-8px, top 4px, radius 6,
  #0969da/#1f6feb; CounterLabel 20px 12px/600 padding 0 8 radius 20 #818b981f/#656c7633; Overlay radius 12, floating small
  shadow, backdrop #c8d1da66/#21283066, 0.2s; TextInput 32px radius 6 1px border focus 1px + 2px outline; kbd marketing
  24.3x20 11px mono radius 4 padding 4px 8px; kbd logged-in 16x16 12px radius 3; Label topic 22px 12px/500 radius 2em,
  issue label 20px 1px border pill; small Button 28px padding 0 8 radius 6 12px/500 #f6f8fa/#212830 border #d1d9e0/#3d444d;
  Pagination 32px radius 6.
- GitHub tokens light/dark: fgColor-default #1f2328/#f0f6fc; fgColor-muted #59636e/#9198a1; fgColor-accent #0969da/#4493f8;
  bgColor-default #fff/#0d1117; bgColor-muted #f6f8fa/#151b23; borderColor-default #d1d9e0/#3d444d; borderColor-muted
  #d1d9e0b3/#3d444db3; bgColor-accent-muted #ddf4ff/#388bfd1a; borderColor-accent-emphasis #0969da/#1f6feb;
  control-transparent-bgColor-hover #818b981a/#656c7633; -selected #818b9826/#656c7633; highlight-neutral-bgColor
  #fff8c5/#d2992266; syntax-entity #6639ba/#d2a8ff; syntax-constant #0550ae/#79c0ff. Radius 3/6/12. Breakpoints 768,
  1012, 1400.

## F. Issues/Projects filter bar (2025)
- Beta 2024-10-01 (https://github.blog/changelog/2024-10-01-evolving-github-issues-public-beta/), GA 2025-04-09
  (https://github.blog/changelog/2025-04-09-evolving-github-issues-and-projects/), Projects 2026-07-16
  (https://github.blog/changelog/2026-07-16-advanced-search-for-projects-is-generally-available/). Rules
  (https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/filtering-and-searching-issues-and-pull-requests):
  AND/OR + parens up to 5 levels; space = AND; comma inside qualifier = OR; warns on problems. Build: PEG grammar, OR at root,
  AND/OR highlighted, 5-level limit "sweet spot" (https://github.blog/developer-skills/application-development/github-issues-search-now-supports-nested-queries-and-boolean-operators-heres-how-we-rebuilt-it/).
  Warning text user-reported "Filter contains 1 issue: Invalid value … for label"
  (https://github.com/orgs/community/discussions/160942). combobox-nav: combobox/listbox + aria-activedescendant, Up/Down,
  Enter, Tab accepts by default (https://unpkg.com/@github/combobox-nav/README.md).

## Decisions to copy (agent's 34)
1 box-styled button; "/" and "S" open; shortcut in aria-hidden kbd + aria-label. 2 icon at mid widths, into menu on narrow.
3 overlay anchored so input covers trigger (-13 top, +15 right inset; logged-in top:-14 left:-14 width:+26). 4 modal dialog,
focus trap, backdrop, 12px radius, small floating shadow, 0.2s fade + 8px slide, reduced motion. 5 <=1011 full-width top
sheet, no top corners, <=80dvh. 6 qualifiers coloured by aria-hidden mirror behind transparent-text input. 7 keys plain,
values accent on accent-muted radius 3, operators purple. 8 combobox/listbox/aria-activedescendant; focus stays in input;
nothing active until Down. 9 12px/600 muted group headings, visual only, group name in each option's label. 10 trailing hint
on every row ("Jump to", "Autocomplete", "Search all of GitHub", "Search in this repository"). 11 active row neutral fill + 4px
accent bar outside left edge (same as current sidebar item). 12 search-scope rows. 13 value completion with dots + fuzzy.
14 polite atomic live region "N suggestions.". 15 footer "Search syntax tips" (+ feedback, Beta logged in). 16 saved searches
(name <=50 + query <=500, up to 10, saved:name). 17 Esc -> focus trigger; Clear -> focus input. 18 whole query in URL (q,
type, s, o, p); facets rewrite q. 19 three columns: 296 resizable sidebar, fluid main, 340 aside, 16/24 padding. 20 types with
counts incl zeros, abbreviated pills, sr full count. 21 facets: languages with dots, Advanced rows insert key + focus input,
Exclude (NOT …) on hover. 22 count + muted ms; hidden h2 focused after search. 23 Sort = menu of radio items with check,
"Sort by:". 24 6px-radius bordered cards, 16px padding, 16px gaps. 25 matches bold <em> (700 titles, 600 text); code yellow
<mark>. 26 "Show N more matches". 27 pagination capped 100 pages, first/last + ellipsis. 28 loading: thin top progress bar,
keeps selected type, skeletons. 29 empty state: illustration, names type, tips, advanced-search link, other types that
matched. 30 invalid input not an error for non-code; code search warning banner. 31 narrow: type switcher + Filter button
-> bottom sheet. 32 header search shows current query styled. 33 dark = tokens only. 34 syntax: whitespace AND, OR loosest,
parens <=5, -key:value, ranges, /regex/, unparseable = literal + warnings that don't block.
