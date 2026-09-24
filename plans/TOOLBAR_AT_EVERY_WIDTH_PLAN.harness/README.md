# Harness for `TOOLBAR_AT_EVERY_WIDTH_PLAN.md`

First pass measured against `2843018a` (3.27.2) on 2026-09-22 (sections A to J). Second pass against
`753b8790` (3.29.0) on 2026-09-24 (sections K to S), where the five shapes regenerate byte-identical
to the first pass's apart from the version stamp and the clock (`python bytediff.py out out-head`).
Everything runs from this folder. Output folders (`out*/`, `shots/`), result files and logs are
git-ignored here.

```bash
# 1. Five report shapes from ReportGenerator, into ./out (builds src/Kronikol for net10.0 once)
KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off dotnet run gen.cs
#    and the stress shape (long CI strings), into ./out-stress
KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off dotnet run gen.cs -- stress
# 2. The width sweep, 320-1400 in 10 px steps, reload at every width; writes sweep-results-out.json
node sweep.js 10 out
# 3. Painted colours at 1400 px: the short probe (cascade.js) and the full census (cascade2.js)
node cascade.js out
node cascade2.js out,out-patched > cascade2-table.md
# 4. The scenario toolbar's shape: rows.js at 1400 (both states), rows2.js at 1400/880/850/800
node rows.js
node rows2.js
# 5. The prototype: the plan's rules applied to copies of the pages, then 2 and 3 again on them
node patch.js 1100 out-patched && node sweep.js 10 out-patched && node cascade.js out-patched
# 6. The D5 variant: everything but the band wrap
node patch.js 0 out-noband && node sweep.js 10 out-noband TestRunReport.html,TestRunReport_noci.html
# 7. Long CI strings: HEAD, the prototype, and the prototype with the CI cap (S1b)
node sweep.js 20 out-stress TestRunReport_longci.html
node patch.js 1100 out-stress-patched out-stress && node sweep.js 20 out-stress-patched TestRunReport_longci.html
CI_WRAP=1 node patch.js 1100 out-stress-patched-wrap out-stress && node sweep.js 20 out-stress-patched-wrap TestRunReport_longci.html
# 8. Font stress (the E2E lane runs on Ubuntu; Verdana is wider than its DejaVu Sans)
INJECT_CSS='body,button,input,select{font-family:Verdana}' TAG=verdana node sweep.js 20 out-patched TestRunReport.html,TestRunReport_noci.html,Specifications.html
# 9. Screenshots: the header at given widths, and the first scenario's toolbar
node shots.js out-patched TestRunReport.html 770,900,1000,1050,1100,1200,320
node tshots.js out-patched TestRunReport_iflow.html 1400,850
```

Second pass (sections K to S):

```bash
# 10. The shapes again at HEAD, plus every toolbar control (-- full); OUT_DIR names the folder
OUT_DIR=out-head dotnet run gen.cs && OUT_DIR=out-stress-head dotnet run gen.cs -- stress && dotnet run gen.cs -- full
python bytediff.py out out-head                      # identical but for the version stamp and the clock
# 11. The published reports (BreakfastProvider, 3.29.0), copied into ./out-published as
#     xunit_TestRunReport.html, xunit_Specifications.html, lightbdd_TestRunReport.html from
#     https://lemonlion.github.io/BreakfastProvider/reports/{xunit,lightbdd}/...
# 12. The final design by string surgery (breakpoint 1160, lower bound 768.02, no search rule)
for src in out-head out-stress-head out-full; do BAND_MIN=768.02 NO_SEARCH_MIN=1 CI_WRAP=1 node patch.js 1160 out-final $src; done
BAND_MIN=768.02 NO_SEARCH_MIN=1 CI_WRAP=1 node patch.js 1160 out-published-final out-published
# 13. sweep2.js: ENGINE=chromium|firefox|webkit, SCROLLBARS=1, MODE=reload|shrink|grow, INJECT_CSS, MIN/MAX, TAG
node sweep2.js 20 out-full                           # HEAD, every control
bash deep2.sh                                        # the conditions battery on HEAD and the first prototype (1100)
bash deep2-final.sh                                  # the same on the final design, and the published reports
node summ2.js final                                  # one line per page and run, from sweep2-results-*.json
# 14. Probes: clipped controls and the top bar at chosen widths; media queries at fractional widths
node probe-local.js out-full TestRunReport_full.html 540,780,1000,1400
node fractional.js out-head-patched TestRunReport.html
# 15. The C# prototype: prototype.diff applied in a detached worktree, gen.cs and s5.cs run there
#     (gen.cs copied beside it so its #:project path resolves to the worktree), then
python bytediff.py out-head <worktree>/plans/p2-harness/out-s3
```

The scripts use the E2E project's own Playwright driver
(`tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package`), so the E2E project must
have been built once and its browsers installed. Node 25 was used; any recent Node works. `patch.js`
takes `[breakpoint] [outDir] [inDir]`; `CI_WRAP=1` adds the S1b cap. `sweep.js` takes
`[step] [dir] [pages]` and reads `INJECT_CSS` (a style tag added after load) and `TAG` (the results
file suffix).

Traps met while writing it:

- A .NET 10 file-based app defaults to `PublishAot=true`, which disables reflection-based
  `System.Text.Json`, and the generator throws inside `DiagramContextMenu.GetPlantUmlBrowserRenderScript`
  (the two `#:property` lines in `gen.cs` are the fix).
- The Node Playwright API evaluates a *string* as an expression, so a string holding `() => {...}`
  returns nothing (the scripts wrap each string in `eval('(' + s + ')')` to get a function; the .NET
  API takes function strings as the suite writes them).
- At 768 px and below `report-init-script.js` hides every scenario toolbar (`display: none`) behind a
  "Diagram Settings" button. A sweep that does not show them measures nothing of the scenario toolbar
  there; `OPEN_ALL` in `sweep.js` sets them to `flex`.
- Buttons do not inherit the body font. `INJECT_CSS` on `body` alone left every label at the
  user-agent font (the export button measured 130 px under "Verdana" and 142 once the rule named
  `button` too).
- A `flex-shrink: 0` item is never squeezed, so `overflow-wrap` on its cells does nothing until a
  `max-width` bounds the box (§F, the third row).
- A first prototype appended the toolbar base rules *after* the moved theme block and read the tab
  blue on the violet pages: the base rules must precede the theme, which is where `stylesheets.css`
  puts them.
- A shell heredoc through the tool ate `\\` in `replace(/\\/g, '/')`; the scripts now split on
  `path.sep` instead of a regex. It eats them inside a Python heredoc too (`"\\n"` in a C# string
  became a real newline; a `'\\r\\n'` normalisation silently matched nothing): write such scripts with
  the editor, as `bytediff.py` is.
- `.feature` and `.scenario` carry `content-visibility: auto`. Paint containment clips what overflows
  them and keeps it out of the page's scroll width, so a sweep that reads only `scrollWidth` cannot
  see a clipped toolbar control (`sweep2.js` measures controls against their toolbar and containers
  against their content). Off-screen containers are also skipped: a probe run later than the sweep
  can read a different answer for a scenario far down the page; force `content-visibility: visible`
  or scroll the scenario into view before asking.
- Playwright's Chromium launches with `--hide-scrollbars`; `SCROLLBARS=1` drops it
  (`ignoreDefaultArgs`). Injected `::-webkit-scrollbar` CSS does not bring the scrollbar back.
- Chromium's `--window-size` with `--force-device-scale-factor` gives fractional CSS widths but
  cannot be aimed at 768.8; Firefox's `layout.css.devPixelsPerPx` with a Playwright viewport can, and
  its `--width` argument is ignored.
- A tap in a touch context leaves the element `:hover` until the next tap elsewhere; screenshot
  before any other tap or the stuck state is gone.
- A fresh `git worktree` on a `core.autocrlf=true` machine checks the `.js` resources out with CRLF,
  and `plantuml-worker-host.js` is embedded raw: a byte diff across two checkouts differs in
  `WORKER_HOST_SOURCE`. `bytediff.py` normalises it.
- The first pass's `sweep.js` summary printed the first and last width of a wrap and read as one band;
  `sweep2.js` prints the bands.

## A. The width sweep at HEAD (`sweep.js 10 out`)

Fixture: two features, six scenarios, six tags, no categories, three diagrams declaring one actor and
seventeen participants (synthetic names, 9 to 31 characters, 256 in total, median 13, one in five a
`database`), CI metadata on the run-report shapes (branch `main`, repository `example/BreakfastProvider`).
`BrowserJs` rendering; the engine fetch blocked. Re-run after the phone toolbars were made visible:
identical bands.

| Shape | Sideways scroll | Overflow at 770 / 900 / 1000 / 1060 | Export labels wrap | Cluster outside the box | Box width at 770 / 900 / 1000 / 1100 / 1200 |
|---|---|---|---|---|---|
| `TestRunReport.html` (CI) | 770-1060 | +295 / +165 / +65 / +5 | 320-380, 770-1260 | 770-1070 | 32 / 125 / 225 / 325 / 425 |
| `TestRunReport_noci.html` | 770-900 | +141 / +11 / 0 / 0 | 320-380, 770-1110 | 770-910 | 149 / 279 / 379 / 479 / 579 |
| `TestRunReport_iflow.html` (CI, flow) | 770-1060 | as above | 320-380, 770-1260 | 770-1070 | as above; scenario-toolbar labels wrap inside their buttons 770-890 |
| `Specifications.html` (violet) | none | | 320-380 | never | 754 / 884 / 984 / 1084 / 1184 |
| `Specifications_iflow.html` | none | | 320-380 | never | as above; scenario-toolbar labels wrap 770-890 |

Culprit at every overflowing width: `div.filtering-box-export`. Top-bar (`.toolbar-row`) labels, on these pages without a component diagram (§L has one),
never wrap inside a button. At 760 px and below (the column layout) nothing overflows.
`.diagram-toggle` computes `display: block` on the three pages without flow tracking, `flex` on the two
with it. At 320 px the box's inner width is 256 px and the widest export button 101 px (wrapped);
the CI box is 288 px at 320 and 274 at 1400.

## B. Painted colours at HEAD (`cascade.js out`), at 1400 px

| Control | `TestRunReport` | `TestRunReport_iflow` | `Specifications` (violet) | `Specifications_iflow` (violet) |
|---|---|---|---|---|
| `.details-radio-btn.details-active` | 66,133,244 | 66,133,244 | 66,133,244 | 66,133,244 |
| same, hover | 66,133,244 | 66,133,244 | 66,133,244 | 66,133,244 |
| `.diagram-toggle-btn.diagram-toggle-active` | 240,240,240 (UA) | 66,133,244 | 139,92,246 | 66,133,244 |
| same, hover | 240,240,240 | 51,103,214 | 139,92,246 | 51,103,214 |
| `.diagram-toggle-btn` | 240,240,240 | 245,245,245 | 240,240,240 | 245,245,245 |
| same, hover | 240,240,240 | 232,240,254 | 240,240,240 | 232,240,254 |
| `.iflow-toggle-btn.iflow-toggle-active` | 240,240,240 | 66,133,244 | 139,92,246 | 66,133,244 |
| same, hover | 240,240,240 | 51,103,214 | 124,58,237 | 51,103,214 |
| `.export-btn`, hover | 230,240,255 | 230,240,255 | 230,240,255 | 230,240,255 |

Violet intends 139,92,246 at rest and 124,58,237 on hover for an active control, and 237,233,254 for
an idle hover. The tab and flow-toggle probes are synthetic buttons appended to the top bar; the
Details radio and the export button are the page's own. §G is the full census.

## C. The D5 variant (`patch.js 0 out-noband`): the width rules and the toolbar rules, no band wrap

| Shape | Sideways scroll | Export labels wrap | Cluster outside the box |
|---|---|---|---|
| `TestRunReport.html` (CI) | 770-920, +160 at 770 (`.filtering-box-export`) | never | 770-930 |
| `TestRunReport_noci.html` | 770 only, +6 (`table.feature-summary-table`) | never | 770-780 |

## D. The prototype (`patch.js 1100 out-patched`)

All five shapes, every width 320-1400 at 10 px and again at 20 px: no sideways scroll, no label
wrapped inside any export, top-bar or scenario-toolbar button (the phone toolbars shown), the cluster
inside its box. Header row at 900 px: `row/wrap`, box 884 px; at 1000 px box 984; at 1200 px in-row,
425 px with CI metadata, 579 without, full width on the specifications shapes. `.diagram-toggle`
computes `flex` on every page. At 320 px the widest export button is 130 px (whole) in a 256 px box.

Colours after the prototype: §G, the `out-patched` columns.

## E. The scenario toolbar at 1400 px (`rows.js`)

Both `TestRunReport.html` (block) and `TestRunReport_iflow.html` (flex), before and after the
prototype: `data-layout="inline"`, 24 px tall, five visible controls from `.details-radio` to
`.note-width-select`, spacer present. The float shrink-wraps in both states, so the F5 fix changes
nothing on a wide window but the padding §H records; the difference is in the stacked state.

## F. Long CI strings (`gen.cs -- stress`, `sweep.js 20`)

`TestRunReport_longci.html`: the §A data with the branch
`feature/KRON-1234-reconcile-nightly-settlement-batches-across-regions` (69 characters), the
repository `my-organisation/breakfast-provider-integration-tests` (52) and build `20260922.17`.

| Rules applied | Sideways scroll | Export labels wrap | Cluster outside the box | Box at 900 / 1000 / 1200 | CI box at 1400 |
|---|---|---|---|---|---|
| HEAD | 780-1320, +558 at peak (`.filtering-box-export`) | 320-1400 | 780-1340 | 32 / 32 / 152 | 547 |
| prototype (S1 + S2 + S3 + S4) | 1120-1200, +83 | never | 1120-1200 | 884 / 984 / 152 | 547 |
| prototype + `.ci-metadata td { overflow-wrap: anywhere }` only | 1120-1200, +83 | never | 1120-1200 | 884 / 984 / 152 | 547 |
| prototype + cap: `.ci-metadata { max-width: 20em }`, `table { max-width: 100% }`, `td { overflow-wrap: anywhere }` | none | never | never | 884 / 984 / 347 | 352, but the label cells broke ("Branch" / ":") |
| prototype + S1b: the cap in `@media (min-width: 769px)`, `td:first-child { white-space: nowrap }`, `td:last-child { overflow-wrap: anywhere }` | none | never | never | 884 / 984 / 347 | 352, labels whole |
| ordinary `TestRunReport.html` + S1b | none | never | never | 884 / 984 / 425 | 274 (unchanged) |

At 768 px and below the long-CI page never overflowed at HEAD: the column layout bounds the box and
the cells wrap at hyphens.

## G. The census (`cascade2.js out,out-patched`, `cascade2-table.md`)

Thirty-seven control states, synthetic elements carrying the real classes, background / border
(`BLUE` = 66,133,244; `BLUE-dk` = 51,103,214; `BLUE-dk2` = 50,110,220; `BLUE-tint` = 230,240,255;
`BLUE-tint2` = 232,240,254; `BLUE-tint3` = 240,244,255; `BLUE-tint4` = 210,227,252; `BLUE-bd` =
100,150,255; `violet` = 139,92,246; `violet-dk` = 124,58,237; `violet-tint` = 237,233,254;
`violet-bd` = 167,139,250; `ua-grey` = the user agent's 240,240,240; "not on these pages" = the
fixture has no real element with those classes, so the row is the class's paint, not a control the
default page shows). The blue-report columns are identical before and after except the tab rows F5
styles. The lines behind the blue values: `stylesheets.css` 374-381 (filter toggles), 398 and 415
(mode toggles), 484-491 (percentile), 504-505 (collapse-expand-all), 547 (copy name), 566 (scenario
link), 591-592 (export), 1180 (failure-cluster link), 1492-1501 (timeline), 1533-1546 (parameter
table); `collapsible-notes-styles.css` 11-12; `internal-flow-popup-styles.css` 71-73, 124-126, 137,
144, 148.

| Control | out/Specifications | out/Specifications_iflow | out/TestRunReport | out-patched/Specifications | out-patched/Specifications_iflow | out-patched/TestRunReport |
|---|---|---|---|---|---|---|
| happy-path idle, hover | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd |
| happy-path active (not on these pages) | violet/violet | violet/violet | BLUE/BLUE | violet/violet | violet/violet | BLUE/BLUE |
| happy-path active, hover | **violet-tint/violet-bd** | **violet-tint/violet-bd** | BLUE/BLUE | violet/violet | violet/violet | BLUE/BLUE |
| dependency idle, hover | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd |
| dependency active, hover (not on these pages) | **violet-tint/violet-bd** | **violet-tint/violet-bd** | BLUE/BLUE | violet/violet | violet/violet | BLUE/BLUE |
| dep-mode, hover | **220,230,245/BLUE-bd** | **220,230,245/BLUE-bd** | 220,230,245/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | 220,230,245/BLUE-bd |
| status idle, hover | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd |
| status active, hover (not on these pages) | **violet-tint/violet-bd** | **violet-tint/violet-bd** | BLUE/BLUE | violet/violet | violet/violet | BLUE/BLUE |
| category idle, hover (not on these pages) | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd |
| category active, hover (not on these pages) | **violet-tint/violet-bd** | **violet-tint/violet-bd** | BLUE/BLUE | violet/violet | violet/violet | BLUE/BLUE |
| cat-mode, hover (not on these pages) | **220,230,245/BLUE-bd** | **220,230,245/BLUE-bd** | 220,230,245/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | 220,230,245/BLUE-bd |
| percentile idle, hover | **BLUE-tint/BLUE-bd** | **BLUE-tint/BLUE-bd** | BLUE-tint/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd |
| percentile active (not on these pages) | **violet/BLUE** | **violet/BLUE** | BLUE/BLUE | violet/violet | violet/violet | BLUE/BLUE |
| percentile active, hover | **violet/BLUE** | **violet/BLUE** | BLUE/BLUE | violet/violet | violet/violet | BLUE/BLUE |
| collapse-expand-all, hover | **BLUE-tint/BLUE-bd** | **BLUE-tint/BLUE-bd** | BLUE-tint/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd |
| timeline idle, hover | **BLUE-tint/BLUE-bd** | **BLUE-tint/BLUE-bd** | BLUE-tint/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd |
| timeline active (not on these pages) | **BLUE/BLUE** | **BLUE/BLUE** | BLUE/BLUE | violet/violet | violet/violet | BLUE/BLUE |
| timeline active, hover | **BLUE-dk2/BLUE-bd** | **BLUE-dk2/BLUE-bd** | BLUE-dk2/BLUE-bd | violet-dk/violet-bd | violet-dk/violet-bd | BLUE-dk2/BLUE-bd |
| export, hover | **BLUE-tint/BLUE-bd** | **BLUE-tint/BLUE-bd** | BLUE-tint/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd |
| details radio idle, hover | **BLUE-tint/BLUE-bd** | **BLUE-tint/BLUE-bd** | BLUE-tint/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd |
| details radio active | **BLUE/BLUE** | **BLUE/BLUE** | BLUE/BLUE | violet/violet | violet/violet | BLUE/BLUE |
| details radio active, hover | **BLUE/BLUE** | **BLUE/BLUE** | BLUE/BLUE | violet/violet | violet/violet | BLUE/BLUE |
| diagram tab idle (not on these pages) | **ua-grey/black** | grey245/grey204 | ua-grey/black | grey245/grey204 | grey245/grey204 | grey245/grey204 |
| diagram tab idle, hover | **ua-grey/black** | **BLUE-tint2/grey204** | ua-grey/black | violet-tint/grey204 | violet-tint/grey204 | BLUE-tint2/grey204 |
| diagram tab active (not on these pages) | violet/violet | **BLUE/BLUE** | ua-grey/black | violet/violet | violet/violet | BLUE/BLUE |
| diagram tab active, hover | violet/violet | **BLUE-dk/BLUE** | ua-grey/black | violet-dk/violet | violet-dk/violet | BLUE-dk/BLUE |
| diagram settings (phone), hover (not on these pages) | **BLUE-tint/BLUE-bd** | **BLUE-tint/BLUE-bd** | BLUE-tint/BLUE-bd | violet-tint/violet-bd | violet-tint/violet-bd | BLUE-tint/BLUE-bd |
| flow toggle idle, hover (not on these pages) | ua-grey/black | **BLUE-tint2/grey204** | ua-grey/black | violet-tint/black | violet-tint/grey204 | ua-grey/black |
| flow toggle active (not on these pages) | violet/violet | **BLUE/BLUE** | ua-grey/black | violet/violet | violet/violet | ua-grey/black |
| flow toggle active, hover | violet-dk/violet | **BLUE-dk/BLUE** | ua-grey/black | violet-dk/violet | violet-dk/violet | ua-grey/black |
| related list item, hover (not on these pages) | violet-tint/violet | **BLUE-tint3/BLUE** | none/black | violet-tint/violet | violet-tint/violet | none/black |
| related summary row, hover (not on these pages) | none/black | **BLUE-tint3/black** | none/black | violet-tint/black | violet-tint/black | none/black |
| scenario link, hover | BLUE-tint/grey200 | BLUE-tint/grey200 | BLUE-tint/grey200 | BLUE-tint/grey200 | BLUE-tint/grey200 | BLUE-tint/grey200 |
| copy scenario name, hover | BLUE-tint/grey200 | BLUE-tint/grey200 | BLUE-tint/grey200 | BLUE-tint/grey200 | BLUE-tint/grey200 | BLUE-tint/grey200 |
| failure cluster link (colour) (not on these pages) | BLUE | BLUE | BLUE | BLUE | BLUE | BLUE |
| param table row, hover (not on these pages) | BLUE-tint2 at 0.11 alpha | BLUE-tint2 at 0.35 alpha | as left | unchanged | unchanged | unchanged |
| param table row active (not on these pages) | BLUE-tint4 | BLUE-tint4 | BLUE-tint4 | unchanged | unchanged | unchanged |

Bold: wrong for a violet page and fixed by the prototype. The last five rows are not toolbar
controls and are the plan's Q3.

## H. The scenario toolbar's geometry (`rows2.js`)

`TestRunReport.html` (block at HEAD) and `TestRunReport_iflow.html` (flex at HEAD), the first
scenario's `details.example-diagrams` opened; `rows` counts distinct child tops (the Details radio
group sits a pixel off the buttons, so a one-row toolbar reads 2), `height` is the truth.

| Page, rules | Width | Layout | Padding | Height | Labels wrapped inside buttons | Controls after the spacer |
|---|---|---|---|---|---|---|
| HEAD block | 1400 | inline | 0 | 24 | 0 | on the line, controls 559-1343 |
| HEAD block | 880 | stacked | 0 | 24 | 0 | on the line |
| HEAD block | 850, 800 | stacked | 0 | 49 | 0 | the width select on a second line at the left |
| HEAD flex | 1400 | inline | 16 px | 24 | 0 | on the line, controls 543-1327 |
| HEAD flex | 880, 850, 800 | stacked | 16 px | 39 | **3** ("Headers Shown", "Databases Shown", "Truncate") | held at the right edge, squeezed |
| prototype, both | 1400 | inline | 16 px | 24 | 0 | on the line, controls 543-1327 |
| prototype, both | 880, 850, 800 | stacked | 16 px | 49 | 0 | on a second row at the left (x = 124 / 94 / 118 px) |

(The "labels wrapped" column counts buttons only; the radio group container always reads one extra
rect and is excluded here.)

## I. Font stress (`INJECT_CSS`, `sweep.js 20 out-patched`)

`body, button, input, select { font-family: Verdana }` on the prototype pages, three shapes: no
sideways scroll, no wrapped label, the cluster inside the box at every width. The CI box grows from
274 to 306 px at 1400, the in-row box at 1200 shrinks from 425 to 340 (526 without CI), the widest
export button at 320 px is 142 px in a 256 px box. With the font on `body` only the labels did not
change (buttons do not inherit), which is the trap above.

## J. Screenshots (`shots.js`, `tshots.js`, in `shots/`)

`out-TestRunReport-900` (HEAD: the 125 px sliver), `out-patched-TestRunReport-{770,900,1000,1050,1100,1200,320,380}`
(the prototype; the void beside the CI group is about 150 px at 770 and 900, about 300 at 1050; the
box in-row and clean at 1101 and 1200), `out-stress-TestRunReport_longci-1200` (HEAD, the 547 px CI
box), `out-stress-patched-wrap-TestRunReport_longci-{900,1101,1200,600}` (S1b: the branch broken at a
hyphen, labels whole; 600 unchanged), `toolbar-out-TestRunReport_iflow-850` (labels wrapped inside
buttons), `toolbar-out-patched-TestRunReport_iflow-850` (rows), `toolbar-out-TestRunReport-{1400,850}`
(the block toolbar), `toolbar-out-patched-TestRunReport-850`.

---

Second pass, 2026-09-24, at `753b8790` (3.29.0). "First prototype" is the 1100 px design of the first
pass plus `.toolbar-left { flex-wrap: wrap }` (the `-patched` folders); "final design" is the plan's
§3 (breakpoint 1160, lower bound `768.02px`, no `#searchbar` rule; `out-final`, `out-published-final`).
`sweep2.js` columns: sideways scroll (`scrollWidth` past `clientWidth`), labels wrapped inside export,
top-bar and scenario-toolbar buttons, the export cluster outside its box, **toolbar controls past
their own toolbar's edge**, and **containers (`.feature`, `.scenario`) holding content wider than
themselves**; the last two see what `content-visibility: auto` clips.

## K. The search input

At HEAD `#searchbar` is 15 px wide at 780 px on the CI shape and 98 px on the no-CI shape: it shrinks
with the box, because it is a flex item of `.filter-search` with `width: 100%`, which caps its
automatic minimum. The first prototype without `#searchbar { min-width: 0 }` (`NO_SEARCH_MIN=1`,
`out-*-patched-nosearch`) measured the same as with it on every shape at every width, in Chromium and
in Firefox (narrowest input 227 px at 320, 206 at 1120 on the long branch). The rule is dropped.

## L. Every toolbar control (`gen.cs -- full`, `probe-local.js`)

| Page, HEAD, Chromium | Sideways scroll | Export wrap | Top-bar wrap | Scenario labels wrap | Controls past the toolbar's edge | Clipped inside a scenario |
|---|---|---|---|---|---|---|
| `TestRunReport_full.html` | 780-1060 (+285) | 320-380, 780-1260 | **500-580** | **780-1400** | **780-1280, up to 505 px** | 780-1260 |
| `TestRunReport_full_longci.html` | 780-1320 (+558) | 320-380, 780-1400 | 500-580 | 780-1400 | 780-1280 | 780-1260 |
| `Specifications_full.html` | none | 320-380 | never | 780-1400 | 780-1280 | 780-1260 |

`probe-local.js` at 780 px: the toolbar is `flex/nowrap`, 41 to 739 px, and its last control ends at
1244: Assertions, Steps, Databases and the three selects past the edge, seven labels wrapped; the
page's scroll width is 1065 (the export cluster), the clipped controls add nothing to it. At 1000: four
past the edge ("Databases Shown" cut through its middle in the screenshot); at 1200: the width select;
at 1400: none. At 540 px the top bar's four left-hand buttons ("Expand All Features", "Expand All
Scenarios", "Scenario Timeline", "Component Diagram") are 121, 127, 112 and 127 px wide, two lines
each. The first prototype without the `.toolbar-left` rule still wrapped them at 500-580; with it,
every column of this table reads never or none. Screenshots `clip-out-full-TestRunReport_full-{780,1000,1200}`,
`clip-out-full-patched-TestRunReport_full-780`.

## M. The conditions battery (`deep2.sh`: HEAD and the first prototype)

| Condition | HEAD | First prototype |
|---|---|---|
| Chromium, classic scrollbar (`SCROLLBARS=1`, 15 px) | CI: scroll 780-1060 (+300), export 320-400 and 780-1280, cluster 780-1080; no CI: 780-920 (+146); flow shapes: the five-control toolbar clipped at 780-800; full shapes clipped 780-1280 | clean, every shape (search >= 212 at 320) |
| Chromium, narrowed from 1400 without reload | the reload bands | clean, every shape |
| Chromium, widened from 320 without reload | the reload bands, plus scenario labels wrapped 780-860 on the pages WITHOUT flow tracking (the phone script's inline `display: flex` stays on every toolbar) | clean, every shape |
| Firefox 148 | CI: scroll 780-1060 (+282), export 320-360 and 780-1240; full: clipped 780-1200, top bar 500-560 | clean, every shape |
| WebKit 26.4 | CI: scroll 780-1040 (+276), export 320-360 and 780-1240; full: clipped 780-1240, top bar 500-540 | clean, every shape |

Injected `::-webkit-scrollbar { width: 17px } html { overflow-y: scroll }` in Playwright's default
Chromium left `clientWidth` equal to the window: `--hide-scrollbars` wins, so only a launch without it
measures the scrollbar.

## N. The published reports (`out-published`, `out-published-patched`, `out-published-final`)

BreakfastProvider's `reports/xunit/TestRunReport.html`, `reports/xunit/Specifications.html` and
`reports/lightbdd/TestRunReport.html` from `lemonlion.github.io/BreakfastProvider/`, 2026-09-24,
generator 3.29.0 (`a557133c`), 8.0, 7.8 and 9.1 MB. 158 scenario toolbars, up to ten buttons each.
One sweep at 20 px takes 46 to 87 s per page.

| Page, Chromium | Sideways scroll | Export wrap | Top-bar wrap | Scenario labels wrap | Controls clipped | Clipped inside a scenario | Box at 900 / 1000 / 1100 / 1200 |
|---|---|---|---|---|---|---|---|
| xUnit run report, HEAD | 780-1080 (+310) | 320-380, 780-1280 | 500-580 | 780-1400 | 780-1300 (+522) | 780-1280 | 100 / 200 / 300 / 400 (CI box 281) |
| xUnit Specifications, HEAD | none | 320-380 | never | 780-1400 | 780-1300 | 780-1280 | full width |
| LightBDD run report, HEAD | 780-1080 (+310) | as xUnit | 500-580 | 780-1400 | 780-1300 | 320-1280 | as xUnit |
| each, first prototype | none | never | never | never | never | 780-840 (LightBDD 320-840) | 884 / 984 / 1084 / 400 on the run reports (360 at 1160); full width on Specifications |

What stays clipped after the prototype is parameterized-test content. At 800 px on the xUnit report
the grouped `table.param-test-table`, `display: table`, is 783 px wide in a 752 px scenario (720 px of
content box), a direct child of the scenario with no scrolling wrapper; at 768 px and below it is a
block scroll container and fine. At 400 px on the LightBDD report `div.param-detail-panels` holds 757
px of content in a 336 px scenario (the table itself scrolls). Both are clipped by the scenario's
`content-visibility: auto`: the plan's Q7.

## O. Text spacing and the breakpoint edge

WCAG 1.4.12 override injected: `*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}`.

| Breakpoint | Condition | CI report | Long branch |
|---|---|---|---|
| 1100 | text spacing | scroll 1120 (+10), cluster 1120; box 183, "Export Filtered HTML" 185 px | scroll 1120-1140 (+29), cluster 1120-1140; box 163 |
| 1100 | text spacing + scrollbar | scroll 1110-1140 (+35), cluster 1110-1150 | scroll 1110-1160 (+54), cluster 1110-1170 |
| 1100 | scrollbar | clean (search >= 259 at 1110) | clean (>= 181) |
| 1140 | text spacing | clean | cluster outside the box at 1150, no scroll |
| 1160 | text spacing | clean (search >= 171 at 1170) | clean (>= 152) |
| 1160 | text spacing + scrollbar | clean (>= 156) | cluster edges out at 1170 only, no scroll (>= 137) |
| 1160 | scrollbar | clean | clean |

(1080 to 1240 px in 10 px steps, `out-bp{1100,1140,1160}`, lower bound 768.02.) At 320 px the text
spacing makes the run summary table (`.test-execution-summary table`, four columns) 19 px too wide, at
HEAD and after every prototype: not a toolbar element (Q7's audit). Screenshots at the 1160 breakpoint:
`out-bp1160-TestRunReport-1150` (in the band, about 390 px of empty space right of the CI group) and
`out-bp1160-TestRunReport-1161` (in the row, the box about 370 px).

## P. A tap on a phone

Chromium context `isMobile`, `hasTouch`, 390 × 844, device scale 3; the violet `Specifications.html`;
tap the mobile filter toggle, then "Happy Paths Only". At HEAD the button is `happy-path-toggle
happy-path-active`, still `:hover`, background `rgb(237, 233, 254)`, text `rgb(255, 255, 255)`, until a
tap elsewhere restores `rgb(139, 92, 246)`. With the prototype (string surgery and C#) it is
`rgb(139, 92, 246)` throughout. Screenshot `tap-out-head-happy-path-sticky`.

## Q. The final design (`deep2-final.sh`)

| Run | Nine synthetic shapes | Published reports |
|---|---|---|
| Chromium, reload, 10 px (published: 20 px) | clean: no scroll, no wrap, no cluster escape, no clipped control, no clipped content; search >= 227 at 320 | no scroll, no wrap, no cluster escape, no clipped control; clipped content only the parameterized tables (780-840; LightBDD 320-840, §N); box 884 / 984 / 1084 / 400 at 900 / 1000 / 1100 / 1200 |
| Chromium, scrollbar | clean (search >= 212) | see the note under this table |
| Chromium, narrowed and widened without reload | clean | |
| Firefox 148 | clean | |
| WebKit 26.4 | clean | |
| Text spacing | clean but the run summary table at 320 (+19) on the six run-report shapes; search >= 162 at 1180 | |
| Text spacing + scrollbar | clean but the run summary table at 320-340 (+34); search >= 147 at 1180 | |
| Verdana on body and controls | clean (search >= 213 at 1180 on the long branch) | |

The published reports with a classic scrollbar (`SCROLLBARS=1`, 20 px): no sideways scroll, no label
wrapped, no toolbar control clipped, on all three; the box 869 / 969 / 1069 / 385 at 900 / 1000 / 1100
/ 1200 on the run reports (the scrollbar's 15 px); search >= 212 at 320. The parameterized content of §N
is clipped a little further, 780-860 and up to 96 px (LightBDD 320-860, up to 508).

## R. Fractional widths (`fractional.js`)

| Browser | Setting | CSS width | `max-width: 768px` | `min-width: 769px` | Header, 769 bound | Header, 768.02 bound |
|---|---|---|---|---|---|---|
| Firefox | `devPixelsPerPx` 1.25, viewport 769 | 768.8 | no | no | row/nowrap, +158 (long branch +431; HEAD +290) | band, 0 |
| Firefox | 1.75, 769 | 768.967 | no | no | +158 (+431) | band, 0 |
| Firefox | 1.5, 769 | 769.333 | no | yes | band, 0 | band, 0 |
| Firefox | 1.1, 769 | 769.083 | no | yes | band, 0 | band, 0 |
| Chromium | `--force-device-scale-factor=1.25`, `--window-size=961` | 961.6 (`innerWidth` 962) | | | | |
| Chromium | 1.5, window 1153 | 1153.333 | | | | |

Chromium's `--window-size` is in whole CSS pixels, so it cannot be aimed at 768.8; Firefox's
`--width` was ignored (the window stayed 1366 device pixels).

## S. The C# prototype (a detached worktree at `753b8790`, since removed)

`prototype.diff` applied: `stylesheets.css` (+37), `internal-flow-popup-styles.css` (−14),
`Constants/Stylesheets.cs` (the reorder and the S4 rules), `ReportGenerator.cs` (`UserStylesheets`,
the two call sites, `combinedStylesheet + "\n"`, `{{themeStyles}}`).

- Byte diff (`bytediff.py out-head <worktree out-*>`, the worktree's CRLF worker source normalised):
  with `combinedStylesheet = HtmlReportStyleSheet` alone, `TestRunReport.html`, `_iflow` and `_noci`
  each lose one line (`\n`), `Specifications*.html` are a pure reorder; with `+ "\n"`, the three no-sheet
  pages show 0 changed lines outside the clock and `Specifications*.html` a reorder plus one byte.
- `s5.cs` (`CreateStandardReportsWithDiagrams`, markers): flow on, `Specifications.html` notes sheet
  at 66858, popup sheet at 71221, THEME 74040, POPUP 74050, CUSTOM 74145; `TestRunReport.html` POPUP
  74033 < CUSTOM 74128, no THEME; flow off, no POPUP in either.
- Nine shapes regenerated through it (at the 1100 bound) swept clean with the string surgery's widths;
  `cascade2.js out-patched,out-csharp` differs on one row, the parameterized-row hover read
  mid-transition (alpha 0.345 against 0.114).
- `dotnet test tests/Kronikol.Tests` (final design): 5,482 passed, 1 skipped, 0 failed, 1 min 36 s.
  `dotnet test tests/Kronikol.Tests.EndToEnd --filter` without WikiGif, WikiScreenshot and Showcase:
  778 passed, 0 failed, 6 min 42 s.
- "Export Filtered HTML" clicked on `out-final` pages: the downloaded file carries `.toolbar-left {
  flex-wrap: wrap; }`, the `768.02px`-to-1160 block, the toolbar base with its wrap and (on the
  specifications page) the theme after the notes and popup sheets; `out-exported` sweeps clean.

## T. Execution (3.29.2, 2026-09-24)

The release was built in a worktree re-checked out with LF line endings (a fresh worktree on this machine
checks out CRLF, and C# raw strings take their line breaks from the source file), and 3.29.1 in a second
LF worktree at `afeafa5b` with `gen.cs` and `violet.cs` copied beside it.

```bash
# the nine shapes from each build (OUT_DIR=out-base in the 3.29.1 worktree)
OUT_DIR=out-release dotnet run gen.cs && OUT_DIR=out-release dotnet run gen.cs -- stress && OUT_DIR=out-release dotnet run gen.cs -- full
# each build's violet theme text, then section 3.3's byte check
dotnet run violet.cs -- new-violet.css          # and old-violet.css from the 3.29.1 worktree
python s3bytes.py <3.29.1>/out-base out-release old-base.css ../../src/Kronikol/Reports/stylesheets.css \
  old-popup.css ../../src/Kronikol/Reports/internal-flow-popup-styles.css old-violet.css new-violet.css
# the release's shapes, with and without classic scrollbars
SCROLLBARS=1 TAG=release-sb node sweep2.js 20 out-release && node sweep2.js 20 out-release
```

- `s3bytes-result.txt`: the six shapes without a custom sheet are identical to 3.29.1's once the base and
  internal-flow sheets are swapped for the new ones; the three violet shapes are identical once the old
  and the new theme text are cut out of each (the theme moved, nothing else did).
- `sweep2.js` on `out-release`: every shape clean on every column, with and without scrollbars.
- The guard (`ViewportSweepTests`) and the painted-colour facts (`ToolbarStylingTests`), run against 3.29.1
  by copying the test files into the 3.29.1 worktree with a stub `UserStylesheets`: 23 of 75 red, the
  three sweep facts with 280, 1,050 and 1,315 problems. On the release: 75 of 75 green, the sweep 57
  widths in about 9 s a page with a 15 px scrollbar at every width.
