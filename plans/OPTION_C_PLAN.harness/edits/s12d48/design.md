# S12 D48: focus is never left under the pinned bar (round 17 item 20), prepared and not applied

> Measured after this was written (2026-10-19 and 20, on `f077cda6`; the plan's log has the numbers): fact 5,
> the Firefox one, passed before the change (risk 1 came true), so it is a guard; facts 1 and 2 needed the fixture
> to wrap the diagram's linked label in an `<a>`, which the browser engine never draws. The red set was facts 1, 3,
> 7 and 12, each on its assertion, and all 14 mutations were killed.

Prepared on 2026-10-19 as a dry run. Base: branch v5 at `a1235d98` (`C:/Code/Kronikol-v5`, read with `git show`). The
branch has since moved to `d385c715` (phone part 1, the 4.32.0 merge, D43, R17.18); the dry run was repeated there and
gives the same edits, 14 lines further down `ReportGenerator.cs` (section 12). Nothing in any worktree was changed, and
no dotnet build, dotnet test or Playwright run was made. The Playwright and core facts below are therefore unrun: the
red and green expectations in section 7 are reasoned, not measured.

| File | md5 at `a1235d98` | md5 at `d385c715` | after (at `a1235d98`) |
|---|---|---|---|
| src/Kronikol/Reports/ReportGenerator.cs (UTF-8 BOM, LF; both kept) | d32cfefa40f50f61afe675d379ca36ed | 61b130ec82099f3d80dd403a78365c01 | 32b18ed5a9a5b07aed7c4a9fb22f670d (2e43ffdf... at `d385c715`) |
| src/Kronikol/Kronikol.csproj (read only: the `Reports\report-*.js` wildcard, line 94) | 13bf295afe101eae199d317015e9f437 | same | unchanged |
| src/Kronikol/Reports/report-focus-script.js (new, LF, no BOM, 91 lines, 5,926 bytes) | | | c9b392425ee4203af95d7c03c748c911 |
| tests/Kronikol.Tests/Reports/FocusScriptTests.cs (new) | | | d1f2d743d9c968a192fd00be7c457e63 |
| tests/Kronikol.Tests.EndToEnd/FocusClearOfTheBarTests.cs (new) | | | 668d3a128a3bcf4bfdf6bc2a1fc1e86b |

Apply with `python scripts-s12d48.py <root>` and then `python tests-s12d48.py <root>` (and, at commit time,
`python changelog-s12d48.py <root>`, section 13). Each checks every anchor (exactly once) in memory, writes nothing
unless all hold, and refuses a second run. `scripts-s12d48.py` reads the new script from
`report-focus-script.js` beside it, so the two travel together into `plans/OPTION_C_PLAN.harness/edits/`, and the test
script reads its two `.cs` files the same way. Copy `checks/mutate-s12d48.py` and `checks/focus-script-check.js` into
`plans/OPTION_C_PLAN.harness/checks/` (the mutation script roots itself on its own path there).

**Stacking with the other S12 stages.** `ReportGenerator.cs` is the only shared file. At `a1235d98` D48 inserts 13 lines
after line 1602 (the components script), changes line 1670 (the head's last script line, `{{componentsScript}}` gains
`{{focusScript}}`) and replaces lines 1937 to 1945 (the tabs' comment and their four flags) with a 2-line comment; at
`d385c715` the same three places are 1616, 1684 and 1951 to 1959. D49's script (`../s12-d49/scripts-s12d49.py`) edits the
`ReportTab(...)` lines (1961 to 1965 at the base), after the last D48 anchor and not inside it, and does not touch the
head; phone part 2 edits line 1817 (the filter box); D43 is `context-menu-script.js` only. The stages stack in any
order: each script finds its anchors by their text, and a moved anchor stops it before it writes.

## 1. What the preview does (`plans/OPTION_C_PLAN.harness/mock/c-round13.js` 58 to 114)

- Comment 58 to 64: the page's scroll padding covers every scroll the browser makes, but Firefox makes none for a
  control that already shows in part, so a feature heading reached by Shift+Tab stayed half under the bar; Firefox also
  leaves a diagram link that Tab reaches below the window where it is (WCAG 2.4.7). A control in the bar, or in a popup
  or sheet over the page, is left alone.
- `floats(t)` 67 to 70: true when the control or an ancestor is `position: fixed`.
- `clear(t, nav)` 71 to 83: only while `.toolbar-row.report-nav` is sticky or fixed; the free band is from the bar's
  bottom + 12 to the window's height - 12, **whether or not the bar is pinned**; a control taller than the band that
  shows 24 px or more inside it is left alone (line 80, round 17); a control above the band scrolls to its top, one
  below it to its bottom (never past its top).
- 86 to 88: `pointerdown` stamps a time, `keydown` clears it; 92: focus within 1000 ms of a pointer press is left alone
  (round 17 item 20).
- 89 to 113: on `focusin` outside the bar, a frame later `clear`; then for a second every scroll and timers at 150,
  400 and 800 ms check again; a wheel, a touch or the control's `focusout` ends the watch, and it ends at 1 s.

## 2. What the product does today (file:line at `a1235d98`)

- The bar: `stylesheets.css` 777 to 779, `position: sticky; top: env(safe-area-inset-top, 0px); z-index: 40`; static on a
  phone (2139). Written by `ReportGenerator.cs` 1956 to 1957 after `<div class="nav-mark">`, only when `useTabs`
  (1942 to 1945: a timeline, a Components, History or Diagnostics tab).
- The scroll margins: `stylesheets.css` 847 (`.report-panels, .report-panels *`), 65, 71, 89, 92, 1230, 1232, all
  `scroll-margin-top: calc(var(--kron-nav-h, 0px) + 12px)`; no bottom margin. `--kron-nav-h` is set by
  `report-tabs-script.js` 72 to 75 (`Math.round` of the bar's height while it is sticky, else 0). So Chromium already
  brings a control in the panels below the bar when it scrolls one into view.
- S8's open settings: `collapsible-notes-styles.css` 107 to 110, `position: sticky; top: var(--kron-nav-h, 0px)` on a
  desktop: they stick right under the pinned bar while their diagrams are in view. No margin counts them, so a control
  after them can sit under them. Their gear comes after their options bar (`ReportGenerator.cs` 1193 to 1194) and sits
  6 px below their top (`margin: 6px ...`, `collapsible-notes-styles.css` 116 to 117).
- S7: `report-filter-bar-script.js` 249 to 256 (`scrolled()`: `is-pinned` while the mark is above the stick line by more
  than 0.5 px, `k-floating` from the line itself); the empty state's Clear All gives the search focus with
  `preventScroll` (158); Filters gives the box's first control focus (214) and the toggle back (231), both with
  `preventScroll`.
- Fixed surfaces: the internal-flow popup's overlay (`internal-flow-popup-styles.css` 1 to 9, appended to the body by
  `internal-flow-popup-script.js`, which gives nothing focus), jump to failure (`stylesheets.css` 649 to 652), back to top
  (2000 to 2006), the context menu (`context-menu-styles.css` 2). Every other control the pinned bar can cover comes
  after the bar's mark, so inside `.report-panels`, which has the scroll margin.
- Keys: `report-keyboard-navigation-function.js` 4 to 8 (`/` gives the search focus by script), 14 to 28 (the arrow keys
  scroll smoothly to the next scenario, which is marked, not focused).
- A scenario is `<details class="scenario" ... tabindex="0">` (`ReportGenerator.cs` 2402), so a click on anything in an
  open scenario that takes no focus itself gives the scenario the focus: R17.20's mechanism.
- Not ported: no focus script; no `focusin` logic in `src/` beyond `report-step-status-titles.js` (titles only).

## 3. The port (`src/Kronikol/Reports/report-focus-script.js`)

- **Which focus.** Only focus that Tab or Shift+Tab moved: a capture `keydown` listener on the window raises a flag on
  `Tab` and lowers it with `setTimeout(..., 0)`. The browser moves focus as the key's default action, in the same task,
  so the flag is up for that `focusin` and down for any focus a click or a script gives later.
- **The band.** Its top is the bar's bottom while the bar is pinned (the mark above the bar's stick line by more than
  0.5 px, S7's own test), else the top of the window; then, if a scenario's open settings are sticky and cover that
  line (stuck under the bar, or being pushed up past it by the end of their section), and the control comes after them
  in the page and is not inside them, their bottom. Plus 12 px. The band's bottom is the window's height - 12.
- **What moves.** A control above the band scrolls to its top; one below it scrolls up to its bottom, never past its top.
  A pixel's grace each way: the scroll margins round the bar's height, the band does not.
- **What stays.** A control taller than the band that shows 24 px or more in it (the preview's rule); a control in the
  bar (the filter box riding in it included); a control in open settings while they stick at their line (pushed up by
  the end of their section, they move with the page, and a control in them is moved like any other); a control in
  anything fixed; everything on a page whose bar is not sticky (a phone) and on a page with no bar (no script there).
- **The watch.** A frame after focus lands, `clear` runs once. For a second after, every scroll and timers at 150, 400 and
  800 ms run it again. The watch ends at 1 s, at the control's `focusout`, and at a wheel, a touch, a pointer press or
  any key.
- **Where it loads.** `<script id="kronikol-focus">` in the head, after `kronikol-report-tabs` and the settings, timeline
  and components scripts, in a page with the bar only. The tab flags that say whether there is a bar move from just
  before the bar to above the head (section 6).

## 4. Decisions asked for

**(a) S8's sticky settings bar.** Taken as recommended: the band starts below whichever is lower, the pinned bar or a
scenario's open settings stuck under it that the control is not inside. Three refinements came out of the node model.
Settings count only where they cover the band's top line (stuck under the bar, or pushed up past it by the end of their
section); open settings further down the page cover nothing. They count only for a control that comes after them: the
scenario's own heading, above them in the page, is never under them. And a control inside them is left alone while they
stick at their line: without that rule, Tab to the gear (6 px under the bar, inside the 12 px) scrolled the page a few
pixels on every scroll event until the settings came unstuck, since a stuck bar does not move with the page (fact 2 and
the tenth mutation hold it). Once the end of their section pushes them up, they move with the page, and a control in
them is moved like any other (a node check and a probe hold that).

**(b) S7's empty state.** Clear All gives the search focus with `preventScroll`; that is a script's focus, which the
helper leaves where it lands, so S7's choice holds and nothing scrolls to the search. The same holds for S7's Filters
(214, 231) and for `/`. Whether the empty state itself should bring the search into view when Clear All was pressed from
the keyboard is owner question 5; it is S7's code, not D48's.

**(c) Tests that focus from script.** They hold by construction: the helper acts only on focus Tab moved, and no
existing test presses Tab (checked at `a1235d98` and at `d385c715`: no `PressAsync("Tab")` or `"Shift+Tab"` in any test).
FeaturesToolsTests 89, 171, 247, ReportTabsTests 56 and ScenarioSettingsTests 113, 206, 212 press arrow keys, Home,
End and Enter, which raise no flag; StepStatusTooltipTests and FilterBarTests focus by `FocusAsync` and `FillAsync`.

## 5. Deviations from the preview

1. **Tab only, not "no pointer in the last second".** The preview moved any focus not within 1 s of a pointer press, so
   it moved a script's focus too: the empty state's search, the box riding in the bar (were it not in the bar), a
   button's own refocus. Rule T leaves every script's focus where the script put it. Owner question 1.
2. **The bar counts only while pinned.** The preview used the bar's bottom + 12 as the band's top at rest too. At rest the
   bar is in the page below the filter box, so a control above it (the run band, the filter box) was "above the band":
   the preview scrolled the page up to it, and on every scroll in the second after it scrolled again, to the top of the
   page. The port's band starts 12 px below the window's top at rest (owner question 3 on the 12 px).
3. **Open settings.** Counted as in 4(a); the preview never had them.
4. **More ends to the watch:** a pointer press (a press on the scroll bar is pointing) and any key (an arrow key's smooth
   scroll to the next scenario, Page Down, Space), so the helper never pulls the page back against the reader. The
   preview ended it on a wheel, a touch and `focusout` only. Owner question 4.
5. **The first frame's check is cancelled** when the watch ends before it runs; the preview's ran regardless.
6. **No script on a page without the bar,** where the preview's ran and returned. Owner question 6 (a bar-less page's
   open settings).
7. **A pixel's grace** (`r.top < top - 1`): the margins round the bar's height (`Math.round`), so without it a bar of
   135.4 px nudged every control Chromium had placed by 0.4 px.

## 6. What changes where (lines at `a1235d98`)

| File | Where | What |
|---|---|---|
| ReportGenerator.cs | after 1602 | the tabs' comment and the four flags (`hasComponentTab`, `hasHistoryTab`, `hasDiagnosticsTab`, `useTabs`), moved from 1937 to 1945 unchanged, then `var focusScript = useTabs ? "<script id=\"kronikol-focus\">" + LoadResource("report-focus-script.js") + "</script>" : "";` with its comment |
| ReportGenerator.cs | 1670 | `{{scenarioSettingsScript}}{{timelineScript}}{{componentsScript}}{{focusScript}}` |
| ReportGenerator.cs | 1937 to 1945 | the comment's last sentence stays, as a 2-line comment on the tab the page opens on; the flags are gone from here |
| report-focus-script.js | new | section 3 |

The moved flags read `componentDiagramPlantUml` (last assigned at 1389), `history`, `showHistorySection`,
`includeTestRunData`, `showReportDiagnostics`, `diagnostics` (parameters, never assigned) and `hasDurations` (1469), all
before 1602, the same at `d385c715` (1403 and 1483). No stylesheet changes; `Kronikol.csproj`'s wildcard embeds the
script. The generated page grows by 5,926 bytes where it has the bar.

## 7. Tests

**Playwright, `FocusClearOfTheBarTests` (13 facts, collection Reports, 1400 by 900).** The fixture: twelve features of
four scenarios, timed (so the bar), every feature open; Scenario 02.2 has 60 steps; with `diagram`, Scenario 01.1 has a
sequence diagram whose second message is a link (`[[#d48-link 201 Created]]`) and so its own settings; with `flow`, the
internal-flow popup. Keys are real presses; a starting control gets focus by script with `preventScroll`. Every wait
uses `PollingInterval = 200`; there is no forced click and no network mocking. Expected before the product change
(tests copied in, product unchanged):

| # | Fact | Before | Why |
|---|---|---|---|
| 1 | `Tab_to_a_link_under_a_scenarios_stuck_settings_brings_it_12_px_below_them` | red | no margin counts the settings; the link stays 6 px under them |
| 2 | `Tab_onto_the_gear_of_settings_stuck_under_the_bar_leaves_them_there` | green (guard) | asserts on the settings and the gear, not `scrollY`: Chromium may scroll 6 px for the gear's margin, which a stuck bar does not follow |
| 3 | `Tab_to_a_scenario_cut_by_the_foot_of_the_window_brings_it_12_px_above_the_foot` | red | Chromium aligns the scenario's bottom with the window's (no bottom margin) |
| 4 | `Shift_Tab_to_a_scenario_half_under_the_bar_brings_it_below_the_bar` | green (guard) | Chromium's scroll margin already does it |
| 5 | `..._in_Firefox` (Playwright's own Firefox, skipped when missing) | red | Firefox scrolls nothing for a control that shows in part (the preview's finding) |
| 6 | `A_scenario_taller_than_the_window_that_Tab_reaches_stays_where_it_is` | green (guard) | |
| 7 | `A_scenario_pushed_away_in_the_second_after_Tab_is_brought_back` | red | a block inserted a frame and 40 ms after focus, anchoring off |
| 8 | `Clicking_in_and_out_of_a_diagram_half_under_the_bar_leaves_the_page_where_it_is` | green (guard) | R17.20 |
| 9 | `Tab_between_the_bars_controls_leaves_the_page_where_it_is` | green (guard) | |
| 10 | `Tab_into_the_internal_flow_popup_leaves_the_page_where_it_is` (window 1400 by 260) | green (guard) | |
| 11 | `Tab_to_a_control_above_the_bar_at_rest_leaves_the_page_where_it_is` | green (guard) | the preview's at-rest defect |
| 12 | `Shift_Tab_to_the_search_cut_by_the_top_of_the_window_while_the_bar_rests_brings_it_12_px_in` | red | Chromium aligns it with the window's top (it is outside the panels: no margin) |
| 13 | `On_a_phone_Tab_leaves_the_page_where_the_browser_put_it` (400 by 800) | green (guard) | |

Numbered in the file's order. A guard that fails before the change is a fact to rework, not a product bug: see risks 1
to 3.

**Core, `FocusScriptTests` (2 theories, 4 cases).** `A_page_with_the_report_bar_has_the_focus_script_in_its_head_after_the_tabs_script`
("a timeline", "a component diagram"): the bar is there, `script#kronikol-focus` is in the parsed head once, after
`script#kronikol-report-tabs`, and its text is the embedded `report-focus-script.js` byte for byte (red before: no
element). `A_page_without_the_report_bar_has_no_focus_script` ("nothing beside its features", "the specifications
report"): no bar, no element, and no script carries the Tab test (green before: a guard for the gate).

**Node, `checks/focus-script-check.js`.** 19 checks on a fake DOM and a fake clock (section 9).

## 8. Mutations (`checks/mutate-s12d48.py`, 14)

Each undoes one rule, rebuilds, runs the named test and expects it to fail. `--check [root]` checks every anchor is in
its file once and that every mutation of the script still parses (`node --check`); an optional argument runs only the
mutations whose test name contains it.

| Mutation | Test that must fail |
|---|---|
| a click's focus is moved too (the Tab flag not consulted) | fact 8 |
| a control taller than the band that shows in it is moved | fact 6 |
| a control in anything fixed is moved | fact 10 |
| nothing checks again after the first frame (the 150/400/800 ms timers) | fact 7 |
| no room is kept at the window's foot (`GAP = 0`) | fact 3 |
| settings stuck under the bar cover nothing | fact 1 |
| the bar covers the window's top at rest too | fact 11 |
| no room is kept at the window's top while the bar rests | fact 12 |
| the bar's own controls are moved | fact 9 |
| a control in settings stuck under the bar is moved | fact 2 |
| a phone's page is moved (`sticks()` always true) | fact 13 |
| the script is not in the head | core, first theory |
| the script is in every page | core, second theory |
| the script's tag holds another script | core, first theory |

The fixed-ancestor, tall-control, re-check, margin, settings and pointer mutations are the six the brief named. Run
through the node model first (`work/mutants-in-node.py`), none of the 11 script mutations is equivalent to the script:
each fails 1 to 9 node checks.

## 9. The node check

`node checks/focus-script-check.js <report-focus-script.js> [--probe]` runs the script in a fake DOM: boxes in the flow,
sticky (stuck at a line, inside their containing box) or fixed; a pinned bar 135 px tall with its mark; a section with
open settings (sticky at 135, 40 px) holding a gear and a link after them; a popup; a fake clock that runs timers and
frames in order and raises a scroll event a frame after each `scrollBy`. 19 checks: under the bar, past the foot, in
the band; the browser's own scroll first; click focus, script focus (with no key, a frame after a Tab, after another
key); the bar, the popup, stuck settings; tall controls showing and not; the bar at rest (above it stays, cut by the top
comes 12 px in); a phone; no bar; stuck settings as the band's top; settings pushed up by their section's end; settings
in the page, before the control, closed; the pixel's grace; the re-checks at 150, 400 and 800 ms; every scroll for a
second and none after; the reader ending the watch (wheel, touch, pointer, key, focus moving on); a new Tab ending the
last watch. `--probe` applies 16 mutations of the script (the timing paths included: the flag going down, Tab alone
raising it, each end of the watch, every scroll checking) and requires each to fail a check: 16 of 16 caught.

## 10. Risks

1. **Fact 5's red rests on the preview's finding** that Firefox scrolls nothing for a control that shows in part. If
   Firefox 148 does scroll it into view past the margin, fact 5 is a guard, and the change has no Firefox-only red
   fact; facts 1, 3, 7 and 12 are still red in Chromium.
2. **Fact 8 assumes Chromium does not scroll on focus a click gives** (the preview's analysis of R17.20, and Blink's
   behaviour for mouse focus). The diagram box is a scroll container (`overflow-x: auto`) but a narrow diagram does not
   overflow it, so it is not one Chromium makes focusable, and the click focuses the scenario; the brief's note that
   recent Chromium makes scroll containers focusable does not reach it. If the fact fails before the change, rework it.
3. **Fact 10 assumes Chromium does not scroll the page for focus inside a fixed overlay.** It is a guard; a failure before
   the change means the fact needs another surface.
4. **Facts 1 and 2 need the diagram drawn** (`WaitForDiagramSvg(60000)`) and its link to be the next stop after the gear:
   a narrow diagram has no zoom controls (`context-menu-script.js` 911 to 919) and the source has no notes; the fact
   asserts where Tab went before it asserts anything else.
5. **`content-visibility: auto`** on features and scenarios changes heights as they come into view; `PlaceAsync`
   measures again over two frames until the place holds, and fails the fact with a message if it cannot.
6. **Timing.** `WaitPastTheWatchAsync` waits until 1.1 s of page time after the last focus; the helper's last check is at
   800 ms and its end at 1 s on the same clock, so a slow machine delays both alike.
7. **The hoisted flags.** Reading shows nothing between line 1602 and 1937 assigns their inputs; the build confirms it.
8. **obscured-check.js** (`run-regression.sh` 18 to 19) will not report zero `partly`: a scenario taller than the window
   whose top is under the bar is left alone by design, as in the preview.

## 11. Owner questions

1. **Rule T.** The port moves only focus Tab or Shift+Tab moved. The preview moved any focus not within a second of a
   pointer press, a script's focus included. Keep Rule T (recommended), or also move a script's focus that follows a
   key press (Enter on Clear All, `/`)?
2. **The settings band** (4(a)) as implemented: the band starts below a scenario's open settings while they are stuck
   under the bar, for a control after them; a control in them is left alone. Agreed?
3. **12 px at the window's top while the bar rests.** A control cut by the top of the window (only the run band and the
   filter box can be: they are above the bar) comes 12 px in, as at the foot; Chromium alone leaves it at the very top.
   Keep the 12 px (fact 12), or 0 at rest?
4. **What ends the watch.** The preview: a wheel, a touch, the control losing focus. The port adds a pointer press and any
   key, so an arrow key's smooth scroll, Page Down or a press on the scroll bar is never pulled back. Agreed?
5. **The empty state (S7).** Clear All pressed from the keyboard gives the search focus without a scroll, so the focus can
   be above the window, under nothing but out of sight (WCAG 2.4.7). Leave it, or let S7 scroll the search into view when
   the press came from the keyboard?
6. **Pages with no bar** (no timeline and no other tab; the specifications report) get no script. A scenario's open
   settings still stick there, at the top of the window, and can cover a control Tab reaches after them. Rare; leave it
   (recommended), or load the script on every page for the settings alone?
7. **The phone.** Nothing moves on a phone, whose bar scrolls away. The phone's search line sticks at the top of the
   window while the filter box is in view (`stylesheets.css` 2036 to 2043) and can cover a filter control Tab reaches
   there. Left to phone part 2?

## 12. Dry run (2026-10-19)

`dry-base/` holds exactly what the scripts read or edit, from `a1235d98` (`ReportGenerator.cs`, `Kronikol.csproj` and
the two empty test folders); `dry/` is a copy with both scripts applied. `bash dry-run.sh [commit]` repeats it, and
`dry-run.txt` is its output:

- Both scripts ran once (`ok: Reports/ReportGenerator.cs, Reports/report-focus-script.js`; the two test files); each
  refused a second run (`D48 is in already`; `already there: D48 tests are in already`, both exit 1) and `dry/` was
  unchanged by the refusals (md5 per file).
- The written files are the package's own (`cmp`); `Kronikol.csproj` untouched; 26 lines change in `ReportGenerator.cs`
  (`rg-s12d48.diff`), its BOM and LF endings kept, no dash added; every new file LF with no BOM and no dash
  (`checks/text-check.py`).
- `node --check` passes; the node check holds 19 of 19 and catches 16 of 16 probes; `mutate-s12d48.py --check dry`
  finds 14 of 14 anchors once, and every mutation of the script parses.
- On `d385c715` (`dry-v5-base/`, `dry-v5/`, `rg-s12d48-v5.diff`): the same 26 lines change, at 1616, 1684 and 1951; 14 of
  14 anchors.

Found on the way and fixed in the package: Playwright's `Mouse.ClickAsync` takes `float`s (fact 8 passed `double`s);
the gear fact first asserted an unchanged `scrollY`, which Chromium's own scroll for the gear's margin would have broken
before and after the change; in the node model, settings pushed up by the end of their section were still treated as
stuck, which left a gear in them under the bar (now they count only at their line, with a check and a probe); the
Write tool turns a `\u` escape into the character itself, which put a literal BOM character in `scripts-s12d48.py` and
two dashes in `checks/text-check.py` (both now `chr(...)`).

## 13. Draft text for later steps

**Changelog, `[Unreleased]`, under Added** (5.0.0, no bump). `python changelog-s12d48.py <root>` adds it at the end of
the Added list (before `### Changed (breaking)`), refuses a second run, and was dry-run on `CHANGELOG.md` at `a1235d98`
and at `d385c715` (`cl-dry/`): the same 7 lines at line 87 in both, every line within 120 characters, no dash.

- **Focus the keyboard moves is never left under the report bar.** Tab or Shift+Tab to a control the pinned bar covers,
  or a scenario's settings stuck under it, brings the control 12 px below them, and to one cut by the top or the foot of
  the window, 12 px inside it, in Firefox too, which scrolls nothing for a control that already shows in part. For a
  second after, a control the page moves away, as a diagram drawn above it does, is brought back, until the reader
  scrolls, points or presses a key. Focus a click or a script gives stays where it lands, as does a control in the bar,
  in those settings or in the internal-flow popup, and a scenario taller than the window. On a phone, where the bar
  scrolls away, nothing moves.

**Plan log line** (`plans/OPTION_C_PLAN.md`): S12 D48 (R17.20): `report-focus-script.js` in the head of a page with the
bar; Tab or Shift+Tab focus only (a click's and a script's are left alone); the band starts below the pinned bar or a
scenario's settings stuck under it (decision a), 12 px in from the window's edges at rest; a second's watch ended by the
reader; 13 Playwright facts (Firefox one skipping when missing), 4 core cases, 19 node checks with 16 probes, 14
mutations. Fill in the red and green runs and the mutation result.

**Kronikol4J ledger** (`ledger-s10.md`): S12 D48 (R17.20): `report-focus-script.js` is new, written as
`<script id="kronikol-focus">` in the head after `kronikol-report-tabs` (and the settings, timeline and components
scripts) in a page that has the report bar, and in no other page; the generator works out its tab flags above the head
now (output unchanged but for the new element). Kronikol4J writes the 4.x page and has no copy of the script.

**Wiki** (S14, `wiki-s10.md`): `Generated-Reports.md`, "Report Features": a bullet "**Focus stays clear of the report
bar** (5.0.0): Tab or Shift+Tab never leaves the focused control under the pinned report bar, or under a scenario's
settings stuck below it, or cut by the window's edges: it is brought 12 px clear. Focus a click gives, and a control in
the bar or the internal-flow popup, is left where it is; on a phone, where the bar scrolls away, nothing moves." No wiki
page mentions the keyboard today (searched at `450844e`), so this is the only page to touch.

## 14. Running it (after the scripts are copied into the harness)

1. Red: `python tests-s12d48.py <root>`, build, run `FocusClearOfTheBarTests` and `FocusScriptTests`; expect section 7's
   red set (facts 1, 3, 5 if Firefox is installed, 7, 12, and the core first theory's two cases), every guard green.
2. Green: `python scripts-s12d48.py <root>`, rebuild (the script is a new embedded resource), run both classes and the
   neighbours that share the head or the bar: `ReportTabsTests` (core and Playwright), `ScenarioSettingsTests`,
   `FeaturesToolsTests`, `FilterBarTests`, `StepStatusTooltipTests`, `VerdictFencesTests` (it reads every embedded
   script), `StylesheetRulesTests`.
3. `node plans/OPTION_C_PLAN.harness/checks/focus-script-check.js src/Kronikol/Reports/report-focus-script.js --probe`.
4. `python plans/OPTION_C_PLAN.harness/checks/mutate-s12d48.py` (14 runs, each a rebuild; about 30 to 45 minutes).
5. With the harness: `run-regression.sh` lines 18 and 19 (`obscured-check.js` in the installed Chrome through
   `launchInstalled`, and Firefox) on a product page, against the preview's tallies.
