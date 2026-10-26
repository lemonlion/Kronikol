# Draft CHANGELOG entry (SEARCH_PLAN 7.5)

The version is a placeholder until the release is pushed ("taking X now"); the numbers marked {..} are measured in S7.
Every behaviour sentence below needs the fact named beside it in the plan before it is kept (the checklist's rule).

```markdown
## [5.N.0] - 2026-MM-DD

The minor part moves (5.M.x to 5.N.0): a search of its own, built after GitHub's, with new controls (a search button,
a query builder, a Search tab with what to show, where it matched, sort and pages, and lines that go to the match), a
new value of the address's `tab=` and new keys (`type=`, `in=`, `s=`, `o=`, `p=`, `hit=`), and new forms of the query
(`tag:`, `status:`, `feature:`, and `-` before a qualifier). The filter box is hidden until the bar's Filters button
opens it. No option's default changes; what a report shows when it opens does, which the Changed section sets out.

### Added

- **A search button on the title line**, left of the theme switch, as GitHub's header has it: "Type / to search". It
  shows the search applied, with a button that clears it; on a phone it is a magnifier, and once you scroll a floating
  search button stays in reach.
- **A query builder** that `/` or the button opens, holding the search applied: the query with its qualifiers coloured,
  and as you type the search itself, a row that filters the tab you are on instead, the scenarios and features whose
  names match (to jump to), the qualifiers, and each qualifier's values with how many scenarios carry each (statuses,
  tags, dependencies, features, this report's duration percentiles). "Search syntax tips" at its foot. The searches run
  on the page since it opened are listed while it is empty; nothing is stored. Back closes it.
- **A Search tab**, there while a search is applied, laid out as GitHub's results page: on the left what to show
  (Scenarios or Features) and where a result matched (Names, Steps, Diagrams, Errors), with counts, then Status, Tags
  and Dependencies, which are the filter box's own controls, then Advanced, which writes a qualifier into the query; on
  the right the count and the time, a sort (Best match, Report order, Name, Longest first, Shortest first, Failed first),
  the two exports, and ten results a page, each with up to three lines showing where it matched, the match marked: a
  step, a request or response, a query, an error.
- **A result takes you to the match itself**, from its title or any of its lines: its scenario opened, its outline row
  chosen, its steps, failure or diagram opened and drawn, the part of a long diagram that holds it, whatever hid it
  turned on for that scenario alone (Databases for a database call, Headers for a header, Events, Assertions, Steps,
  Setup), the note opened as far as the match needs and a long note's box scrolled to it; the matched words are marked
  where they are drawn, and a line under the diagram's views says what was turned on. Back returns to the results where
  they were; the address keeps the match (`hit=`), so Forward, a reload or a shared link go back to it.
- **New forms in the query**: `tag:name` (as `@name`), `status:failed` (as `$failed`), `feature:text` (the scenarios
  whose feature's name holds it), and `-` before any qualifier to exclude it (`-tag:slow`, `-status:passed`,
  `-dep:payment`). Every documented query means what it meant.
- Two theme tokens, `--kron-mark` and `--kron-mark-edge`, the colours of a marked match, which a custom theme or
  `CustomCss` can set.
- The address keeps the Search tab, what it shows, where it matched, its sort and page, and the match a result went to:
  `tab=search`, `type=`, `in=`, `s=`, `o=`, `p=`, `hit=`.

### Changed

- **The filter box is hidden until Filters opens it**, and the bar's Filters button is always there, with the number of
  filters on. The box opens at the top of the bar with every row showing, and shows the search applied, with its own
  clear: More filters, and the phone's own Filters button, are gone. A script or test that uses a filter row's control
  presses `.nav-filters-toggle` first (5.0.0 told it to press More filters).
- **A page whose only section is its features gets the bar too**, with the Features tab, so Filters and the Search tab
  have a home.
- **The search runs on Enter**, no longer as it is typed. `#searchbar` is now a hidden field holding the search applied
  (what `q=` writes); the field a reader types in is `#search-input` in the query builder. A test that typed into
  `#searchbar` types into the builder and chooses its "Filter" row to stay on its tab.
- The search line leaves the filter box, and the deep-search chip that sat in it: while deep search reads payloads, a
  bar runs along the Search tab's results and their counts carry "…". `window.__kronikolSearch` is unchanged.
- The filters are announced to screen readers by an element of the bar's own, always in the page.
- `data-search-kinds` beside `data-search` on every scenario says where each part of its text starts.

### Fixed

- Enter ran every search twice, 300 ms apart, and results only deep search had found vanished in between.
- A search pasted with the mouse, or dropped, waited until the field lost focus.
- The search help said the instant search reads diagram source; it reads the participants and the paths of HTTP calls,
  and deep search the rest.
- The instant search never read a call's path while internal-flow tracking was on, which is the default, and read a long
  path only to its first 100 characters. `data-search`, `data-row-search` and the search index of such reports change.
- Emptying the search left the outline rows it had marked, and the row it had selected.
- `/` swallowed the key on an exported page, and put the focus behind an open lightbox.
- Back and Forward could return to an arrow of another diagram once a diagram above it had been drawn.
- A scenario's Payloads and Headers choices did not reach its diagrams drawn after them.
- A collapsed note showed a preview of its JSON while the note was shown as YAML.
- On a phone, the search scrolled away with the filter box.
- WebKit drew a long note's scroll box outside its card while the diagram was zoomed or shrunk to fit.
- `NotePayloadFormat`'s summary called JSON the default; the default has been YAML since 4.0.0.
- On a large report, a tab change, a search and a filter change were slower than they need be: the filter controls were
  looked for across the whole page, a dozen times a change, and a tab change laid the page out again for each part
  that measures itself. On a report of 2,000 scenarios a tab change from the Features tab went from about {140} ms to
  about {85}, and Enter to its results lost about {100} ms.

### Tests

- The 37 facts that tested a C# copy of the search are sorted: the pure ones run the shipped scripts under Jint, the
  ones about the page run on a page, and those asserting what the page no longer does are gone; the copy is gone.
- {n} new facts; {m} mutations, each killed (`plans/SEARCH_PLAN.harness/checks/mutations.md`).
```
