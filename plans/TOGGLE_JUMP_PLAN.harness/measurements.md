# TOGGLE_JUMP_PLAN measurements

M1 to M7 were measured 2026-11-07 on main at 896ddad3 (5.31.1). None of the scripts involved changed since 5.27.13, the
version #227 names: `git log v5.27.13..896ddad3` over the context-menu, collapsible-notes, filter-bar, focus, toggle,
diagram-toggle-layout and plantuml-browser-render scripts is empty. M8 to M14 were measured the same day on 601c1dad
(5.32.0), which changed none of them either.

The window is 1366 x 768 unless said (what a 1920 x 1080 screen shows at about 140% page zoom). The browsers, all
headless:

- bundled Chromium 149 (playwright-core 1.61.0);
- Firefox 148 and WebKit 26.4 (playwright-core 1.59.1, the .NET suite's).

Every row is one run of `probe/jump.js`. It draws every scenario, zooms one scenario's diagram (or not) and places it.
It then clicks the report's toggle in the pinned bar with the mouse, and samples the window's scroll and the diagram's
box every frame until the page has been still for 60 frames. Two measures:

- **"moved"**: how far the box of the diagram read moved in the window (positive: down). This is M1 to M7's measure.
  A diagram that grows moves its top even when the reader's place is kept, so "moved" overstates some rows.
- **"text"**: how far the reader's content moved (M8 on). It is the drawn text of the diagram read that stood nearest
  the reading line before the click, found again after by its words and which time they occur in the box. The reading
  line is the middle of the window below the bar and any stuck settings bar: `(kronikol_freeTop(the element at the
  window's middle) + innerHeight) / 2`, 450 px with the bar pinned. 0 is a kept place.
- **"near"**: the median move of every text within 80 px of that line. A diagram that grows 45% spreads the texts
  around a kept point by up to about 45 px each way, so "near" is not 0 even for a kept place.

"writer" is the call stack of each script write to the window's scroll in that time. The probe wraps `scrollBy`,
`scrollTo`, `scroll`, `scrollIntoView` and the root's `scrollTop`.

Fixtures (`fixtures/`, written by `fixtures-writer.cs`, not committed):

| fixture | what it is |
|---|---|
| `wide-long-note` | `ReportTestHelper.GenerateReportWithWideLongNoteDiagram(.., 30, headerLine: true)`: one scenario, one wide diagram (2,553 px natural, fit 48%) drawn whole, a note with a header line; a short page |
| `wide-many` | eight scenarios, each a wide (2,579 px natural, fit 48%) tall diagram drawn whole (1,270 px at fit, 1,961 at 74%), every request note starting with a header line: Headers draws every one again and each grows (1,270 to 1,823 px at fit) |
| `wide-in-parts` | `GenerateReportWithWideDiagramInParts(.., headerNotes: true)`: one diagram in two parts |
| `dense` | `DenseReportFixture.GenerateWithFlows(3, 6, 6, everyFilter: true, internalFlow: false)`: 18 scenarios whose diagrams fit (671 px natural) |

## M1. A zoomed diagram on a short page (wide-long-note, Headers)

`node probe/jump.js fixtures/wide-long-note headers <zoom> <where> 0`

| zoom | where | before: top, height, scroll | after: top, height, scroll | moved | writer |
|---|---|---|---|---|---|
| 74% | mid | 183, 482, 279 | 332, 482, 130 | +149 | `scrollBy(0,-148.78)` < keepPlace L1397 < applyZoomLevel L1545 < addZoomButton L1598 < restoreZoomState L1626 |
| 100% | mid | 58, 652, 404 | 313, 652, 149 | +255 | the same |
| 74% | top | 253, 482, 209 | 332, 482, 130 | +79 | the same |
| 100% | top | 234, 652, 228 | 313, 652, 149 | +79 | the same |
| fit (48%) | mid | 350, 316, 112 | 350, 316, 112 | 0 | none |

The diagram's height is the same after as before. The toggle changed nothing on it that shows (the header line is in
the note's folded part), and the page still moved up to 255 px.

## M2. What the zoom code reads while it does it (M1's first row, `PROBE_RECTS=1`)

| t (ms) | read | top, height, width | window scroll |
|---|---|---|---|
| before the click | the zoomed picture | 183, 482, 1,889 | 279 |
| 63 | addZoomButton: the box | 350, 316, 1,264 | **112** |
| 63 | resizes: the new picture, no inline style (fit) | 350, 316, 1,236 | 112 |
| 64 | placeBefore: the same picture; the window's middle (384) is 10.8% down it | 350, 316, 1,236 | 112 |
| 64 | keepPlace: the picture re-zoomed | 183, 482, 1,889 | 279 |
| 65 | `scrollBy(0, 183 + 0.108 * 482 - 384 = -148.78)` | | 130 |

The swap took the picture's zoomed width with it, and the page was 166 px shorter for that one layout. Its scroll was
clamped from 279 to 112. The reader had been 41.7% down the zoomed picture. Re-zoomed, the picture is back at 183 and
482 px tall, so the move that keeps the place is 0.

## M3. The window's top edge on the diagram above, or not (wide-many, Headers, scenario 3 of 8 read)

`node probe/jump.js fixtures/wide-many headers <zoom> <where> 3` with `BROWSER=chromium|firefox|webkit`.

- "top": the diagram read 80 px below the bar, the window's top edge on the picture above (`elementsFromPoint` at
  y 2: `svg`).
- "cut": the edge 40 px above that picture's bottom.
- "gap": the edge 20 px below it, in the scenario's foot (`DETAILS.scenario`).
- "mid": the diagram read across the window's middle, its top at the window's top.
- "deep:1117": the window's middle 1,117 px down the diagram read.

| where | zoom | Chromium | Firefox | WebKit | writer |
|---|---|---|---|---|---|
| top | 74% | +553 (212 to 765) | +553 (212 to 765) | +554 (212 to 766) | none |
| top | fit | +553 (212 to 765) | +553 | +554 | none |
| cut | 74% | +553 (137 to 690) | | | none |
| cut | fit | +553 (137 to 690) | +553 | +554 | none |
| gap | 74% | -173 | -172 | -172 | keepPlace < applyZoomLevel, `scrollBy(0,+173)` |
| gap | fit | 0 (the scroll +553: the browser's own correction) | 0 | 0 | none |
| mid | 74% | -216 | -216 | -215 | keepPlace < applyZoomLevel, `scrollBy(0,+216)` |
| deep:1117 | 74% | -629 | -628 | -628 | keepPlace < applyZoomLevel |

+553 is the diagram above's own growth (1,270 to 1,823 px). Nothing made up for it, and the diagram read stood 2 or 3
px above the window's foot. With the edge in the gap the browser did make up for it, +553 to the scroll a frame
later. In "mid", keeping the middle point's place in the zoomed picture (19.6% down) needs +174, and the page moved
+216. M8 measures the same rows by the reader's content.

An earlier "deep" row (-844 in Firefox and WebKit) came from a "gap" placement that landed 1,117 px into the diagram
read. The scenario above was still skipped by content-visibility, so its diagram measured where it was not (its box
overlapped the read one). The probe now brings that diagram into view first, and holds a "gap" or "cut" placement only
once the two boxes read in order.

`CLICK=js` (the button's own `click()`, the focus left on the body) gives the same +553 in "top". The focus on the
toggle is not the cause, though `report-filter-bar-script.js` L93 records that Chrome anchors on a focused control.

## M4. Zoomed diagrams above the one read (`ZOOM_ALSO=0,1,2`: the three scenarios above zoomed to 74% first)

`ZOOM_ALSO=0,1,2 node probe/jump.js fixtures/wide-many headers <zoom> <where> 3`

| zoom of the diagram read | where | Chromium | Firefox | WebKit | writer |
|---|---|---|---|---|---|
| fit | gap | -2,052 (76 to -1,976) | -2,052 | -2,961 | `scrollBy(0,+1025.78)` < keepPlace < applyZoomLevel < addZoomButton, once per zoomed diagram above |
| 74% | gap | -2,226 | | | `+173`, then the same |
| fit | mid | -2,052 | | | the same |
| 74% | mid | -2,269 | -2,268 | -3,290 | `+216`, then the same |

What each swap does (`TRACE=1`, the first row; "probe" is the top of the diagram read):

| swap | Chromium and Firefox | WebKit |
|---|---|---|
| s3, the diagram read (not zoomed) | grows below its top: probe 76 to 76 | the same |
| s2, zoomed, just above the window, near the screen | frozen at its height until the swap ends (`minHeight`, CN L3764). The restore's first read lays it out at fit, 138 px shorter, and the browser corrects that (scroll 6,558 to 6,420). The zoom then keeps the box's bottom: +1,026 back to 2,849 px. Net +888, its real growth: probe 76 to 76, **kept** | drawn from the cache, so no freeze: the restore scrolls +1,025 and the probe moves -1,025 |
| s1, zoomed, far above | the restore scrolls +1,026, and the probe moves **-1,026** | +911, probe -911 |
| s0, zoomed, far above | the same, **-1,026** | +1,025, probe -1,025 |

A zoomed diagram far above the window stands in a scenario the browser has not laid out. A feature or scenario card is
`content-visibility: auto` with `contain-intrinsic-size: auto 500px` (`stylesheets.css` L63-64), so the card keeps its
remembered size while it is off screen. The diagram's growth never reaches the page. Its restore still reads the box's
bottom from its own layout, finds it 1,026 px lower (2,849 - 1,823: the height the zoom gives back over the picture at
fit), and scrolls the page by that much for nothing. In Chromium and Firefox two of the three zoomed diagrams do so;
the one just above the window is kept right. WebKit does it for all three.

## M5. A diagram that fits, zoomed out (dense, scenario 8)

| zoom | toggle | before: top | after: top | writer |
|---|---|---|---|---|
| 75% (out) | Steps | 0 | 96 | under held anchoring (applyZoomLevel's hold) |
| 75% (out) | Databases | 0 | 96 | the same |
| 100% | Steps | 0 | 0 | none |

## M6. A diagram in parts (wide-in-parts, Headers)

The container's top stayed at 0 at 74%, 100% and fit. The new parts are sized at the diagram's level while hidden
(`_sizeHiddenParts`, 5.7.3), so the slider made again finds nothing to change and keeps no place. M8's T7 row measures
the reader inside it.

## M7. Where the toggles are at narrow widths (`probe/narrow.js`)

At 1366 px the report's `_toggleHeaders` button is in the pinned bar (`DIV.toolbar-right`). At 760 px it is in
`DIV.toolbar-right#display-settings`, hidden until the bar's display settings are opened. Each scenario's toggles are
in its `.diagram-options-bar`, shown by its settings. By design: not a fault.

## M8. The reader's content, today (every row, three engines)

`node probe/rows.js` (`BROWSER=firefox|webkit` for the others). Rows: M1a/b/c = M1's 74% mid, 100% mid, 74% top. T7 is
the reader 2,000 px into the two-part diagram (`deep:2000`). T9 and T9top are the scenario's own Headers toggle in its
settings bar (`SCOPE=scenario`), the diagram read zoomed, across the middle and 80 px below the bar.

| row | Chromium text (moved) | Firefox text | WebKit text | on screen after |
|---|---|---|---|---|
| M1a | +149 (+149) | +149 | +149 | yes |
| M1b | +255 (+255) | +254 | +255 | yes |
| M1c | +120 (+120) | +119 | +119 | yes |
| M3 top, 74% | +664 (+553) | +664 | +664 | yes, at the window's foot |
| M3 top, fit | +650 (+553) | +649 | +650 | yes, at the window's foot |
| M3 cut, fit | +692 (+553) | +692 | +691 | yes, at the window's foot |
| M3 gap, fit | +166 (0) | +166 | +166 | yes |
| M3 gap, 74% | -17 (-173) | -17 | -17 | yes |
| M3 mid, 74% | -16 (-216) | -17 | -15 | yes |
| M3 deep:1117, 74% | -74 (-629) | -73 | -73 | yes |
| M4 (fit, gap) | -1,886 (-2,052) | -1,886 | -2,795 | **no** |
| M4 mid (74%) | -2,069 (-2,269) | -2,069 | -3,090 | yes (Chromium, Firefox), **no** (WebKit) |
| M5 (dense, Steps, 75% out) | +43 (+96) | +44 | +47 | yes |
| T7 (parts, deep:2000, 74%) | +22 (0) | +22 | +22 | yes |
| T9 (scenario's Headers, mid) | +15 (-207) | +6 (-216) | -15 (-215) | yes |
| T9top | +14 (-97) | +14 | +15 | yes |

M3 gap, fit: the browser keeps the window's top edge, which is in the gap. The diagram read grows below its top, and
the text at the reading line moves down by the part of that growth above it. That is what the browser's anchoring
does, not a fault of its own. Whether the page should keep the top edge or the reading line is the plan's Q1.

## M9. The prototype (`PROTO=1`, probe/proto.js), every row, three engines

The plan's keeper, built at runtime on what the scripts export. The text at the reading line moved:

| row | fraction: Chromium / Firefox / WebKit | landmark: Chromium / Firefox / WebKit |
|---|---|---|
| M1a, M1b, M1c | 0 / 0 / 0 each | 0 / 0 / 0 each |
| M3 top, 74% | +2 / +3 / +4 | 0 / 0 / 0 |
| M3 top, fit | -7 / -8 / -7 | +1 / 0 / +1 |
| M3 cut, fit | +3 / +3 / +2 | +1 / 0 / 0 |
| M3 gap, fit | +3 / +3 / +3 | 0 / -1 / 0 |
| M3 gap, 74% | -13 / -13 / -14 | 0 / +1 / -1 |
| M3 mid, 74% | -4 / -4 / -4 | 0 / 0 / 0 |
| M3 deep:1117 | +19 / +19 / +19 | 0 / 0 / 0 |
| M4 | +3 / +3 / +4 | 0 / -1 / 0 |
| M4 mid | -4 / -5 / -4 | +1 / 0 / +1 |
| M5 | -40 / -39 / -34 | 0 / +1 / +1 |
| T7 | -2 / -3 / -2 | 0 / -1 / 0 |
| T9 | +18 / +9 / -12 | +28 / 0 / -12 (the landmark lost, the fraction used) |
| T9top | +3 / +3 / +4 | 0 / 0 / 0 |

In every run, in every engine, no script but the keeper wrote the scroll during the batch: no `keepPlace` from the
zoom code. The diagram read stayed on screen in all 96 runs.

T9 in Chromium, landmark: the page scrolled 6 px on the click, before any handler ran (scroll 4,602 to 4,596; no script
wrote it). The keeper read its place after it and kept a neighbouring text exactly, 28 px from the probe's in a diagram
that grew 45%. The fraction keeper reads "-40" in M5 because Steps adds rows unevenly down the diagram.

## M10. Each piece turned off (`PROTO_OFF`, landmark keeper)

| off | Chromium rows M1a, M3 top, M3 gap, M3 mid, M4, M5, T9 | what it shows |
|---|---|---|
| `hold` (P3) | 0, 0, 0, 0, 0, 0, +28: the same as on | the keeper reads its place after the browser's own correction, at the same forced layout, so the two never add up. Firefox, M3 top, gap, mid, M4, M4 mid, T9: 0, 0, 0, -1, 0, 0 |
| `p5` (P5) | 0, 0, 0, 0, 0, 0, +28; the zoom code's `keepPlace` ran in six of them (one, one, none, one, three, one, one) | the keeper also puts back what the restore's wrong scroll moved, in the same task, so no frame shows it |
| `take` (no keeper: P5 alone) | 0, +664, +166, **+200**, +166, -53, **+228** | P5 alone fixes M1 and M4's far diagrams, and leaves fault B. It makes M3 mid and T9 worse than today (-16, +15): with no zoom code scrolling, nothing keeps the place of a diagram whose own picture holds the browser's anchor |

## M11. A note's own fold on a zoomed diagram (`toggle=note`)

`node probe/jump.js fixtures/wide-long-note note in mid 0` double-clicks the note nearest the window's middle (the
suite's way to cycle a note's state), which folds it. No keeper runs for a note's own controls.

| | what is painted, frame by frame | after |
|---|---|---|
| today, 74% | the swap; the restore's `scrollBy(0,-128)` (keepPlace < applyZoomLevel < restoreZoomState); **a frame with the diagram 239 px down** (scroll 40); then the note's own rule (CN L414) `scrollBy(+153)` 30 ms later | +111, the page's end clamping the scroll after the fold |
| today, fit | the swap; one frame at +72 (the page's end) | +72 |
| prototype (P5), 74% | the swap; no restore scroll; one frame at +111; the note's rule `scrollBy(+25)` | +111 |

## M12. The landmark lookup's cost (Chromium, landmark keeper)

| run | lookups | longest | all of them |
|---|---|---|---|
| T7 (two parts, 11,465 px) | 1 | 0.3 ms | 0.3 ms |
| M4 mid (eight diagrams of 40 calls) | 8 | 0.3 ms | 1.6 ms |

No lookup was refused or lost in these runs. The one lost lookup in M9 (WebKit, T9) fell back to the fraction.

## M13. A note's fold where it changes no height

`wide-many`'s notes are whole and short. Folding one at fit cycles its state and draws the diagram again with the same
height (1,270 px), so nothing moves, zoomed or not. The note probe uses `wide-long-note`.

## M14. Probe notes

- Content-visibility skips scenarios off screen. A skipped scenario's diagram can measure where it is not, so a
  placement that reads the diagram above must bring it into view first.
- Firefox and WebKit lay out late around a scroll. Every placement is retried until it holds (12 tries at most).
- `kronikol_freeTop(t)` needs an element: called with nothing, it throws (`compareDocumentPosition`). The probe and the
  prototype pass the element at the window's middle.
