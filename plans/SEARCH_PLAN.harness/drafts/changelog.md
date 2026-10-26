# Draft CHANGELOG entry (SEARCH_PLAN 7.5)

The version is a placeholder until the release is pushed ("taking X now"); the numbers marked {..} are measured in S7.
Every behaviour sentence below needs the fact named beside it in the plan before it is kept (the checklist's rule).

```markdown
## [5.N.0] - 2026-MM-DD

The minor part moves (5.M.x to 5.N.0): a search of its own, built after GitHub's, with new controls (a search button,
a query builder, a Search tab with its types, facets, sort and pages), new keys of the report's address (`tab=search`,
`type=`, `s=`, `o=`, `p=`) and new forms of the query (`tag:`, `status:`, `feature:`, and `-` before a qualifier).
The filter box is hidden until the bar's Filters button opens it. No option's default changes; what a report shows
when it opens does, which the Changed section sets out.

### Added

- **A search button on the title line**, left of the theme switch, as GitHub's header has it: "Type / to search". It
  shows the search applied, with a button that clears it; on a phone it is a magnifier.
- **A query builder** that `/` or the button opens: the query with its qualifiers coloured, and as you type the search
  itself, the scenarios and features whose names match (to jump to), the qualifiers, and each qualifier's values with
  how many scenarios carry each (statuses, tags, dependencies, features, this report's duration percentiles). "Search
  syntax tips" at its foot. The searches run on the page since it opened are listed while it is empty; nothing is stored.
- **A Search tab**, there while a search is applied, laid out as GitHub's results page: on the left the result types
  with their counts (Scenarios, Features, Steps, Diagrams, Errors), then Status, Tags and Dependencies to narrow by, each
  with an Exclude, then Advanced, which writes a qualifier into the query; on the right the count and the time, a sort
  (Best match, Report order, Name, Longest first, Shortest first, Failed first), the two exports, and ten results a
  page, each with up to three lines showing where it matched, the match marked: a step, a request or response, a query,
  an error. A line opens its scenario at that step, error or diagram note; Back returns to the results where they were.
- **New forms in the query**: `tag:name` (as `@name`), `status:failed` (as `$failed`), `feature:text` (the scenarios
  whose feature's name holds it), and `-` before any qualifier to exclude it (`-tag:slow`, `-status:passed`,
  `-dep:payment`). Everything written before means what it meant.
- The address keeps the Search tab, its type, sort and page: `tab=search`, `type=`, `s=`, `o=`, `p=`.

### Changed

- **The filter box is hidden until Filters opens it**, and the bar's Filters button is always there, at the top of the
  page, pinned, and on a phone, with the number of filters on. The box opens at the top of the bar with every row
  showing: More filters, and the phone's own Filters button, are gone. A script or test that uses a filter row's control
  presses `.nav-filters-toggle` first (5.0.0 told it to press More filters).
- **A page whose only section is its features gets the bar too**, with the Features tab, so Filters and the Search tab
  have a home.
- **The search runs on Enter**, no longer as it is typed. `#searchbar` is now a hidden field holding the search applied
  (what `q=` writes); the field a reader types in is `#search-input` in the query builder. A test that typed into
  `#searchbar` types into the builder.
- The search line leaves the filter box, and the deep-search chip that sat in it: while deep search reads payloads, a
  bar runs along the Search tab's results and their counts carry "…". `window.__kronikolSearch` is unchanged.
- The filter readout announced to screen readers is the bar's, which is always in the page.
- `data-search-kinds` beside `data-search` on every scenario says where each part of its text starts; `data-search` is
  unchanged.

### Fixed

- Enter ran every search twice, 300 ms apart.
- A pasted or dropped search waited until the field lost focus.
- The search help said the instant search reads diagram source; it reads the participants and the URLs of HTTP calls,
  and deep search the rest.
- `$failed` missed a scenario skipped after a failure, which the Failed button shows; `$status`, `status:` and the Status
  facet now count it as the Status buttons do.
- Emptying the search left the outline rows it had marked, and the row it had selected.
- Clear All, and Back to a page with no search, left open the feature and scenario a one-match search had opened.
- `/` swallowed the key on an exported page, and put the focus behind an open lightbox.

### Tests

- The 37 facts that tested a C# copy of the search now run the shipped script, under Jint; the copy is gone.
- {n} new facts; {m} mutations, each killed (`plans/SEARCH_PLAN.harness/checks/mutations.md`).
```
