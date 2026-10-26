# Red runs on the base: B8 and B17

The base is `c10ed743`, the plan's own commit, whose code is 5.9.0's (`37428293`). Each new fact was written first and
run there, in the worktree `C:/Code/Kronikol-search-tests` (LF checkout), on Playwright's bundled Chromium, one class at
a time.

## B17: `FilterPerformanceTests.The_filter_timings_run_the_shipped_scripts`

Added to the class as it stood, whose page was built by its own `GenerateLargeReport` with its own `fc()` and
`filter_dependencies`, and run alone. Red:

```
Failed Kronikol.Tests.EndToEnd.FilterPerformanceTests.The_filter_timings_run_the_shipped_scripts [435 ms]
Assert.Equal() Failure: Strings differ
                                     ↓ (pos 49)
Expected: ···"cies() {\n    var c = fc(), sets = chip_sets('depen"···
Actual:   ···"cies() {\n    var c = fc();\n    var activeSet = new"···
at FilterPerformanceTests.cs:line 170
```

The failing line, for `filter_dependencies`, the first function the fact compares:

```csharp
Assert.Equal(shipped, await Page.EvaluateAsync<string?>("name => typeof window[name] === 'function' ? window[name].toString() : null", function));
```

`shipped` is the function's text in `report-dependency-filter-function.js` as `ReportGenerator.LoadScript` gives it to
a report without history; the page's function was the class's copy, which hid with a `dep-hidden` class.

Green after the rewrite, with the timings on a generated report: the class passes 5 of 5.

## B8: the 37 facts of `SearchFunctionTests`

A sorting, not a defect of the page (plan 6.1, row B8): each kept fact runs the page's code, so each passes on the base,
and what proves it is that breaking the shipped code its family covers fails it (`../checks/mutations-b8.md`), where
the old facts passed with the same code broken (W1 and H1 there: 37 of 37).

On the base:

- `ShippedSearchFunctionTests` (Jint, `tests/Kronikol.Tests.SearchEngine`): 16 of 16 (9 facts, 16 with the theories'
  rows).
- `SearchFunctionPageTests` (Playwright, `tests/Kronikol.Tests.EndToEnd`): 13 of 13.

No new fact found a defect in 5.9.0's search.
