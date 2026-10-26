# The tests the plan touches (2026-10-26, main 29e551f3, 5.8.0)

Line numbers are at `29e551f3`. "E2E" is `tests/Kronikol.Tests.EndToEnd/`, "unit" is `tests/Kronikol.Tests/Reports/`,
"Jint" is `tests/Kronikol.Tests.SearchEngine/`. Counts are `[Fact]`/`[Theory]` attributes or call sites, as found by
grep; re-count them when S7 starts.

## Helpers

| Helper | Where | Calls | What changes |
|---|---|---|---|
| `FillSearchBar` | E2E `PlaywrightTestBase.cs:356-363` (fill `#searchbar`, dispatch `keyup`) | 58 in 24 files | opens the builder with `/`, fills `#search-input`, Enter, back to the tab it was on unless asked to stay on Search |
| `OpenFilterRowsAsync` | E2E `PlaywrightTestBase.cs:115-127` (clicks `.filtering-box.rows-folded .more-filters` if visible) | 56 in 24 files | opens the box with `.nav-filters-toggle` when it is hidden |
| `#searchbar` locators | 47 in 14 files | | the helper, or `#search-input` |
| `SearchAndWaitForCount` | E2E `AdvancedSearchTests.cs:16-22` | | through `FillSearchBar` |
| `SearchAndWaitForVisible`, `WaitForChipText` | E2E `DeepSearchTests.cs:136-149` | | the chip's text becomes the Search tab's progress and counts |
| `SearchAndWaitFor` | E2E `HistoryFilterTests.cs:25` | | through `FillSearchBar` |
| `SearchAsync` | E2E `CompressedReportTests.cs:97` | | through `FillSearchBar` |
| `HashSoonAsync`, the `data-kron-nav` wait | E2E `BackForwardTestBase.cs:43-48, 144` | | unchanged |

Already script-driven, unchanged: `BrowserRenderWorkerTests.cs:169, 174` and `ZoomRobustnessTests.cs:254` set
`#searchbar.value` and call `run_search_scenarios()`; `#searchbar` stays (hidden), so they keep working.

## Unit facts that pin what changes

- `FilterBarTests.cs`: 54-67 (the phone toggle), 69-81 (`rows-folded` seed, More filters, and the exact order
  `filter-search` > `search-field` > `#searchbar` > `?` > More filters > `#filter-rows`), 91-104 (the exact NavFilters
  markup and Filters' title), 106-117 (the close button), 127-137 (a page without tabs has no bar group).
- `RunBandTests.cs:119` (`StartsWith` the `rows-folded` tag).
- `FilterRowsTests.cs:57-62` (`Box()` slices from the `rows-folded` tag; about a dozen facts at 76-245 use it), 217-226
  (the placeholder, the help rows, the help note).
- `StylesheetRulesTests.cs`: 210-216 (the fold rules; 215 asserts no desktop override), 218-234 (the phone toggle and
  the box's colours), 77-126 (the one breakpoint; 125 requires FB's `'(max-width: 768px)'`), 236-242 (no empty media
  blocks).
- `ControlTitlesTests.cs:337-395` (351: "Show or hide the filters"; 347-350: the field's and the help's titles).
- `ReportTabsTests.cs`: 74-86, 153-165, 167-173 (Specifications without durations has no bar).
- `FocusScriptTests.cs:73-83` (the focus script only with the bar).
- `ReportThemeTests.cs:91-100` (the title line's exact markup); `ThemedPagesHtmlTests.cs:107-126` (the labs page's
  title line, unchanged), 228-246.
- `FormStateRestorationTests.cs:87-99` (`autocomplete="off"`).
- `KeyboardNavigationReportTests` (5 facts: `/`).
- `VerdictFencesTests.cs:37-55, 70-89` (SHA-256 of both variants of the four fenced scripts).
- `NoteCopyFidelityScriptTests.cs:74-89` (the worker's roster).
- `AdvancedSearchReportTests` (4), `TagExpressionSearchReportTests` (5): literal strings in the page.
- `SearchFunctionTests` (37) with `SearchFunction.cs`: moved to Jint on the shipped script (B8).
- Unchanged and to stay so: `SearchIndexReportTests` (18; `DistinctTextCount` 234-264, byte stability 266-272),
  `SearchIndexVectorTests` (5), `SearchNormalizerEquivalenceTests` (2), `SearchDataAttributeTests` (22),
  `UrlHashFilterTests` (6; function bodies 41-121), `UrlAnchoredFilterReportTests` (4), `ClearAllFiltersReportTests` (5),
  `PersistentFilterStateReportTests` (3), `ToggleDefaultsBaselineTests`, `CompressedPageTests.cs:305-319`.

## Jint facts

`JintTestBase.cs:17-34` loads the raw `advanced-search.js`: TokeniserTests 42, ParserTests 30, EvaluatorTests 25,
EdgeCaseTests 22, IntegrationTests 19, BackwardCompatibilityTests 5, VerdictSearchTests 6 (worker roster 106-119),
VerdictDeepSearchTests 2 (73-119), DefaultVariantVerdictSearchTests 2, SearchQualifierPlacementTests 8 (a `document` stub,
20-23), SearchIndexJintTests 18 (three scripts against the shared vectors, 18-21, 35-39). The new forms add to
Tokeniser, Parser, Evaluator and QualifierPlacement; display text and snippets add to SearchIndexJintTests and a new
SearchSnippetTests.

## E2E facts that assume the box shows, Filters only while floating, or the search line

- `FilterBarTests.cs` (15): 77-159, 161-194 (Filters hidden at rest 165, visible floating 168), 196-207 (hidden again
  206), 209-225, 210-260 (empty state), 277-357, 121-129 (help), 162-196 (riding focuses the search), 360-367 (the chip
  inside `.search-field`).
- `MobileFilterLineTests.cs`: all 9 facts (55-174).
- `MobileResponsiveTests.cs`: 78-91, 109-123, 330-365, 367-379 (the sticky search line), 540-567.
- `ViewportSweepTests.cs`: 239-249, 300-319, 336-351 (and 52-60, 85-87, 132-216 for widths and pages).
- `RunBandLayoutTests.cs`: 45-61, 108-137. `ReportSectionWidthTests.cs`: 40-126.
- `FocusClearOfTheBarTests.cs`: 425-466. `ControlTitlesAuditTests.cs`: 14-21, 346-354 (floats the bar, then clicks
  Filters).
- `ThemedPagesTests.cs`: 107-129, 121-126, 151-198. `ThemeTests.cs`: 14 (`button.theme-toggle`; stays unique), 58-152.
- `ReportToolbarTests.cs`: 191-330 (6 search facts). `ReportTabsTests.cs`: 72-84 (Slash).
- `AdvancedSearchTests.cs` (15; help 165-209). `DeepSearchTests.cs` (24; cross-engine 203-219, chip and `#puml-data`
  443-566, telemetry 570-592). `BackForwardFilterTests.cs` (10; 19, 120). `HistoryFilterTests.cs` (3).
  `UrlHashFilterHighlightTests.cs` (10; `q=` restore 107-117; 101-202). `FilterPerformanceTests.cs` (4, a hand-built
  page, 11-60).
- `FilterPipelineTests.cs` (17; 75-76, 250-271, 330-355, 367, 382). `NotATestPaintTests.cs`: 162, 234, 247.
  `BackForwardRestoreTests.cs`: 169, 195, 348, 393-398, 451-461 (R14). `BackForwardPlacesTests.cs`: 38, 283, 341.
  `FilterRowLabelTests.cs`: 17, 30-31. `TagFilterTests.cs`: 69-259. `ScenarioInteractionTests.cs`: 377-399.
  `ToolbarStylingTests.cs`: 83-106, 300-301. `StableIdDeepLinkTests.cs`: 296-310. `ComponentsTabTests.cs`: 191.
  `TimelineTabTests.cs`: 213. `ZoomRobustnessTests.cs`: 244-262. `CompressedReportTests.cs`: 97, 322.
- On demand or skipped: `TagRowVolumeTests.cs:65`, `ShowcaseReportTests`, `WikiGifTests` (the search GIF, 680-711),
  `WikiScreenshotTests`.
- Performance guard: `DeepQueryMs` under 2000 x stretch (`BrowserRenderWorkerTests.cs:166-174, 282`;
  `ContentionScale.cs:32-39`).

## CI

`.github/workflows/ci.yml:105-107`, the "E2E (Search & Filters)" lane's filter, and 140, the Remainder lane's
`filter-exclude`, kept in step: every new E2E class goes in both in the same commit.
