# Draft Kronikol4J ledger entry (SEARCH_PLAN 7.5)

For `Kronikol4J/docs/REMAINING_PARITY.md`, section "## .NET-side features shipped after this audit (divergence
ledger)", appended after the last entry and before "## Explicitly OUT OF SCOPE", in the form of the 5.5.4 entry
(`origin/main` line 2509 on 2026-10-26). Applied only after the release is published; the version and date are filled
in then. Commit message form: `Ledger: .NET 5.N.0, a GitHub-style search and the filter box behind Filters`.

```markdown
- **A search of its own and the filter box behind Filters (.NET 5.N.0, 2026-MM-DD, `plans/SEARCH_PLAN.md`).** Not
  mirrored: the port's page stays at 3.0.43's, which has the search line and filter box of that release and no deep
  search. Byte parity breaks on every page: the title line gains a search button, the filter box is written hidden at
  the top of the report bar with no search line, More filters or phone Filters line, every page with a filter box gets
  the bar, the Search tab and its panel are written, every scenario gains `data-search-kinds`, and the page carries three
  new scripts and a stylesheet (`report-search-ui-script.js`, `report-search-results-script.js`,
  `report-search-marks-script.js`, `search-styles.css`) with changes to the grammar, search, index, filter-bar, keyboard,
  tabs, navigation and notes scripts; the search index of a report with linked calls changes too (B11). The data files
  (`TestRunReport.json`, `.krkl`, the mergeable file, XML, YAML) are unchanged. What the page gains: a query builder
  with suggestions, a results tab laid out as GitHub's with result types, facets, sort, pages and the lines where each
  result matched, a result line that goes to the match itself (what hides it turned on for the scenario, the words marked
  where they are drawn), four new query forms (`tag:`, `status:`, `feature:`, `-key:`), and new address keys
  (`tab=search`, `type=`, `s=`, `o=`, `p=`, `hit=`). The page's `data-search` gains the paths of linked calls (B11).
```
