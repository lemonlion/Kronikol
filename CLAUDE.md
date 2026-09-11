# CLAUDE.md

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

Kronikol follows **semantic versioning**. All packages must use the same version number.

- **MAJOR** — a breaking change to the public API or to configuration: removing or renaming a public type, member or option, or changing a default so that existing code behaves differently without being touched. Reserved for the planned v4; never bump it without asking first.
- **MINOR** — anything new. A new option, public type or member; a new report control, diagram feature or integration; a new package. **A release that adds a feature is a minor bump even when it also fixes bugs** — the highest-ranking change in the release decides the bump.
- **PATCH** — bug fixes, performance work and internal refactoring. Nothing new for a consumer to call.

A commit that touches only repo documentation, plan files or test comments needs no version bump at all.

Everything through **3.0.85** used a patch bump regardless of content, so the 3.0.x history under-reports its features. That history is not renumbered — the rule applies from the next release onward.

Judgement calls that come up in this repo specifically:

- A change to generated **report output** (HTML, PlantUML source, embedded scripts) is not on its own a major bump, even though it breaks golden pins and Kronikol4J byte parity. Record it in the Kronikol4J divergence ledger and pick the bump from the nature of the change itself.
- A **new configuration option** is a minor bump even when its default preserves today's behaviour exactly — it is new public surface.
- A bug fix that necessarily changes observable behaviour is still a **patch**; call the behaviour change out in the changelog rather than inflating the bump.

After every session of work is complete and the full test suite has passed:

- Increment the version in **all** packages (not just the main one), per the rule above.
- Update the Change Log with a clear description of the changes: new features, bug fixes, breaking changes. **State which part of the version moved and why**, so the number can be checked against the release.
- Commit, create a git tag (`v{version}`), and push both the commit and the tag to origin.

## Documentation

After any changes are made that might affect the public API or functionality, documentation must be updated to reflect those changes. This includes updating the README (if relevant), the changelog, and mainly the wiki at `../Kronikol.wiki`.

## Playwright E2E Test Rules

When writing or modifying Playwright end-to-end tests in `tests/Kronikol.Tests.EndToEnd/`:

- **No `{ force: true }` click bypass** — never use `ClickAsync(new() { Force = true })` or similar. If a click doesn't work, diagnose and fix the root cause (e.g., use JS `dispatchEvent` for SVG elements that intercept pointer events).
- **No network mocking** — do not mock network requests. Tests must use real page rendering with local HTML files.
- **Use `PollingInterval = 200`** on all `WaitForFunctionAsync` calls — the default `requestAnimationFrame`-based polling fails under parallel test execution load.
- **SVG interactions** — use JS `dispatchEvent` for:
  - Context menu: `dispatchEvent(new MouseEvent('contextmenu', ...))` via `DispatchContextMenu()` helper
  - Note hover: `dispatchEvent(new MouseEvent('mouseenter', ...))` (not `mouseover`)
  - Note double-click: `dispatchEvent(new MouseEvent('dblclick', ...))` to avoid `<text>` element pointer interception
- **Strict mode** — always use `.First` or `.Nth(n)` when selectors may match multiple elements (per-diagram buttons, nested summaries, report+scenario level controls).
- **Search bar** — use `FillSearchBar()` helper which dispatches `keyup` event after `FillAsync` (required by `onkeyup="search_scenarios()"`).
