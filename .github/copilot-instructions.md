# Copilot Instructions

## TDD Workflow

- Always use Test-Driven Development (TDD): write tests first, then follow the red-green-refactor cycle.
- Write a failing test (red), implement the minimum code to make it pass (green), then refactor.
- Write additional failing tests to cover edge cases and error conditions, and repeat the cycle until you have comprehensive test coverage for the feature or bug fix you're working on.
- UI features should include Playwright tests to verify the user experience and catch any regressions in the UI layer, not just unit tests for the underlying logic.

## Bug Fixing

- Always fix all bugs you find along the way, even if they are outside the immediate scope of the current task.
- When fixing a bug, identify missing test coverage in and around the affected area and create that coverage — again following the TDD red-green-refactor cycle.
- Fix any additional bugs discovered during that expanded test coverage work.

## Versioning & Release

- After every session of bug fixes or feature updates is complete and the full test suite has passed, increment the patch version in **all** packages (not just the main one)
- All packages must use the same version number.
- The Change Log must be updated with a clear description of the changes made in the new version, including any new features, bug fixes, or breaking changes.
- Commit, create a git tag (`v{version}`), and push both the commit and the tag to origin.

## Documentation

After any changes are made that might effect the public API or functionality, documentation must be updated to reflect those changes.  The documentation should be clear and comprehensive, covering all new features, changes to existing features, and any deprecations or removals.  This includes updating README file (if relevant), the changelog, but mainly the wiki which can be found in a sister folder to the main repository - ../Kronikol.wiki.

## Playwright E2E Test Rules

When writing or modifying Playwright end-to-end tests in `tests/Kronikol.Tests.EndToEnd/`:

- **No `{ force: true }` click bypass** — never use `ClickAsync(new() { Force = true })` or similar. If a click doesn't work, diagnose and fix the root cause (e.g., use JS `dispatchEvent` for SVG elements that intercept pointer events).
- **No network mocking** — do not mock network requests. Tests must use real page rendering with local HTML files.
- **Use `PollingInterval = 200`** on all `WaitForFunctionAsync` calls — the default `requestAnimationFrame`-based polling fails under parallel test execution load. Always specify `PollingInterval = 200` in the options.
- **SVG interactions** — use JS `dispatchEvent` for:
  - Context menu: `dispatchEvent(new MouseEvent('contextmenu', ...))` via `DispatchContextMenu()` helper
  - Note hover: `dispatchEvent(new MouseEvent('mouseenter', ...))` (not `mouseover`)
  - Note double-click: `dispatchEvent(new MouseEvent('dblclick', ...))` to avoid `<text>` element pointer interception
- **Strict mode** — always use `.First` or `.Nth(n)` when selectors may match multiple elements (per-diagram buttons, nested summaries, report+scenario level controls).
- **Filter and search** — two things. To filter the Features list, use the `FillSearchBar()` helper: it opens the filter box where it is closed, fills the filter field `#searchbar` (which filters on `input`), presses Enter and closes the box again. To search, pass `toSearchTab: true`: it opens the query builder (`window.kronikol_search.open()`), fills `#search-input` and presses Enter, landing on the Search tab. The search's query is `search_query()`, never `#searchbar`.
