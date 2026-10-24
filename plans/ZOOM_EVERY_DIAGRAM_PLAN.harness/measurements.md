# Measurements (ZOOM_EVERY_DIAGRAM_PLAN)

Taken 2026-10-23 on the owner's Windows 11 machine (device scale 1), headless Chromium from the Playwright install of
`C:/Code/Kronikol4J/playwright` (`PLAYWRIGHT_CORE` names it for the probes; `launchInstalled` as CLAUDE.md asks, a plain
launch for this bundled build). The report is the consumer report the owner sent on 2026-10-23 for the 5.5.1 work
(`TestRunReportWithSlowToggle.html`, a zip holding a 28,954,978-byte `TestRunReport.html` written by 5.4.4: 512
scenarios, 1,552 diagram boxes). It is not in the repository; a scenario is cited by its index among `details.scenario`,
counted from 0 as `probe-behaviour.js` takes it, never by name, and the parts of a diagram from 1. 5.5.1 to 5.5.3
changed none of the zoom rules measured here (5.5.1 reads the natural width from the picture's `width`, 5.5.3 reads 0
for a picture with no box).

The probes are in `probe/`:

- `probe-widths.js <report> <width> <height> <out.json>` opens every feature and scenario, draws every diagram
  (`window._renderDiagramsInContainer(document.body)`), and writes one row per drawn box: a whole diagram's container,
  or each part of a diagram drawn in parts. Each row has the picture's natural width and height (`svg.width.baseVal`,
  `svg.height.baseVal`), the box's `clientWidth` and its content width (less its padding), and the drawn size.
- `analyse.js <out.json>` reads it. "Wide" is today's rule (`isDiagramZoomable`, `context-menu-script.js` L1032:
  natural width over `clientWidth + 1`), so a wide box is one with a slider today.
- `probe-behaviour.js <report> <scenario> <width> <height>` draws one scenario, brings each box to the middle of the
  window, and dispatches one cancelable Ctrl+wheel (`deltaY -100`) at the middle of the box's visible part. It prints
  whether the page kept the event (not prevented: the browser zooms the page), the picture's text sizes, and how far
  the picture's point that was under the pointer moved across (dx) and down (dy) after the step.

```
PLAYWRIGHT_CORE=C:/Code/Kronikol4J/playwright/node_modules/playwright-core node probe/probe-widths.js <report> 1920 1000 w1920.json
node probe/analyse.js w1920.json
```

## M1. Which boxes fit (probe-widths.js, analyse.js)

1,061 of the 1,613 boxes are not laid out (771 activity diagrams and 290 sequence diagrams whose box is not displayed
with every `details` open; why each is hidden was not looked into). They measure 0 and get a slider only once shown
(5.5.3), so they are counted apart. The 552 laid-out boxes are all sequence diagrams:

| Window | Wide (slider today) | Fit (no slider today) | Wide: fit level min / p10 / median / p90 | Fit: natural width median / p90 |
|---|---|---|---|---|
| 1920 x 1000 | 335 (60.7%) | 217 (39.3%) | 62 / 70 / 81 / 97% | 1,484 / 1,738 px |
| 1366 x 768 | 495 (89.7%) | 57 (10.3%) | 43 / 49 / 58 / 85% | 1,004 / 1,174 px |

Where wide and fitting boxes meet, in page order:

| Window | Scenarios with both | Wide then fitting | Fitting then wide | Diagrams in parts | ... with wide and fitting parts |
|---|---|---|---|---|---|
| 1920 x 1000 | 15 | 16 | 2 | 24 | 15 |
| 1366 x 768 | 13 | 11 | 6 | 24 | 13 |

So the owner's case ("one diagram after another, and the second has no zoom") is mostly one diagram drawn in parts: at
1920 px 15 of its 24 diagrams in parts have a wide part beside a part that fits. Scenario 1 is one: parts of 2,254,
1,004 and 1,716 px natural width in a 1,790 px box, drawn at 79%, 100% and 100%; at 1366 px (a 1,236 px box) at 54.8%,
100% and 72%. The text changes size from part to part of one diagram.

## M2. What the wheel does over each (probe-behaviour.js, scenario 1)

| Window | Part | Natural | Drawn at | Slider | Ctrl+wheel kept by the page |
|---|---|---|---|---|---|
| 1920 x 1000 | 1 | 2,254 x 4,166 | 79.4% | min 79 | no (the part zooms) |
| 1920 x 1000 | 2 | 1,004 x 2,397 | 100% | none | **yes: the browser zooms the page** |
| 1920 x 1000 | 3 | 1,716 x 5,590 | 100% | none | **yes** |
| 1366 x 768 | 1 | 2,254 x 4,166 | 54.8% | min 55 | no |
| 1366 x 768 | 2 | 1,004 x 2,397 | 100% | none | **yes** |
| 1366 x 768 | 3 | 1,716 x 5,590 | 72.0% | min 72 | no |

## M3. Text sizes, and the levels readers already see

The pictures' `<text>` sizes (scenarios 1 and 10): 13 px for messages (718 to 1,329 per part), 11 px for notes (up to
2,885 in a part), 10 px and 14 px (participants). Drawn at a level p, a text paints at its size times p: at the smallest
fit level M1 found, 62% at 1920 px, a note paints at 6.8 px; at 43% (1366 px) at 4.7 px. At 50% a message paints at 6.5
px and a note at 5.5 px; at 25%, 3.3 px and 2.8 px.

Fitting pictures taller than the window, and how many a given lowest level shows whole (natural height times the level
no taller than the window):

| Window | Fitting | Taller than the window | Whole at 75% | at 50% | at 33% | at 25% | Natural height median / p90 |
|---|---|---|---|---|---|---|---|
| 1920 x 1000 | 217 | 171 (78.8%) | 27 | 82 | 99 | 115 | 1,777 / 7,619 px |
| 1366 x 768 | 57 | 31 (54.4%) | 11 | 21 | 24 | 27 | 813 / 2,875 px |

## M4. The point under the pointer after one Ctrl+wheel step (probe-behaviour.js)

| Window | Box | Level before, after | dx (across) | dy (down the page) |
|---|---|---|---|---|
| 1920 x 1000 | scenario 1, part 1 (2,254 x 4,166) | 79.4%, 80% | 6.6 px | 11.7 px |
| 1920 x 1000 | scenario 10, whole (2,680 x 5,900) | 66.8%, 70% | -1.0 px | **91.2 px** |
| 1366 x 768 | scenario 1, part 1 | 54.8%, 57% | **24.4 px** | 42.2 px |
| 1366 x 768 | scenario 1, part 3 (1,716 x 5,590) | 72.0%, 73% | 8.3 px | 26.2 px |

The whole diagram keeps its point across (dx -1 px) and loses it down the page (91 px in one notch). A part loses it
both ways on its first step: `applyZoomLevel` sets the box's `scrollLeft` (L1088) before it makes the box scroll
(`overflowX = 'auto'`, L1107). A whole diagram's container scrolls from the stylesheet (`inline-svg-styles.css` L9:
`.plantuml-browser { overflow-x: auto }`), a part (`.puml-fragment`) does not, so the part's first scroll is lost.

## M5. Pictures in the padding band (analyse.js)

A diagram's container has 1em of padding on each side (`inline-svg-styles.css` L10 and L141); a picture is drawn no
wider than the content box (`max-width: 100%`), but `isDiagramZoomable` and `getFitPercent` (L1032, L1039) compare it
with `clientWidth`, padding included. A picture between the two widths is drawn scaled down with no slider: 10 boxes at
1920 px (the smallest drawn at 99.3%) and 2 at 1366 px (98.1%). A wider picture's slider starts above the level it is
drawn at: scenario 10's slider reads 68% at its lowest while its picture is drawn at 66.8% (M4), so its first step goes
from 66.8% to 70%. Parts have no padding of their own, and are not affected.

## M6. One scale for the parts of a diagram (Q2; analyse in the command below, probe/probe-parts.js)

Asked after the owner's answer to Q2 ("that sounds better, unless there's a reason not to? The scaling differences can
be jarring"). The report holds 24 diagrams drawn in parts, 68 parts, up to 15 in one diagram. With every part of a
diagram drawn at the diagram's fit level (the smallest of its parts'):

| Window | Parts drawn smaller than today | Their loss in points: median / p90 / max | Diagrams whose scale drops while first drawn |
|---|---|---|---|
| 1920 x 1000 | 25 of 68 | 19.6 / 30.5 / 32.6 | 2 (one drop each) |
| 1366 x 768 | 42 of 68 | 15.4 / 50.9 / 52.0 | 6 (one drop each) |

A first drawing queues every part and shows each as it is drawn, top to bottom (RS L1122 to L1145), so a part wider
than every part above it lowers the scale of parts already on screen. A redraw shows its parts only once all are drawn
(CN L2700 to L2745), so it never does. The table's numbers come from `w1920.json` and `w1366.json` (probe-widths.js)
with each diagram's parts in order: `analyse.js` prints them on its last line.

The engine lays each part out on its own, so a participant's lifeline stands at a different place in each part
(scenario 1, lifeline centres in each part's own units; probe-parts.js):

| Part | Natural width | Lifelines at |
|---|---|---|
| 1 | 2,254 | 355, 1,055, 1,133, 1,501, 2,213 |
| 2 | 1,004 | 577, 639, 718, 841, 963 |
| 3 | 1,716 | 434, 496, 574, 942, 1,652 |

One scale makes the text one size; the participants still move from part to part.

A zoom step's layout, one part's picture resized against every part's (probe-parts.js, median of 9 to 15, 1920 x 1000):

| Scenario | Parts | `<text>` in all | One part | Every part |
|---|---|---|---|---|
| 1 | 3 | 5,570 | 4.0 ms | 17.0 ms |
| 492 | 8 | 14,782 | 10.1 ms | 61.7 ms |
| 278 | 15 | 45,943 | 14.0 ms | 191.0 ms |

The slider of a tall box: with the page scrolled 1,500 px into scenario 1's first part, its slider's top is at -1,472
px, off the window, at both sizes. The controls are `position: sticky; top: 2em` (IS L44 to L46), but the container's
`overflow-x: auto` (IS L9) makes its `overflow-y` compute to `auto`, so the container, which never scrolls down, is the
sticky element's scroll box and the slider never follows the page (B4).

```
PLAYWRIGHT_CORE=... node probe/probe-parts.js <report> 1 1920 1000 15
```

## M7. Parts laid out alike (Q7; probe/probe-align.js)

Asked after the owner's question on Q7 ("Surely there's no way to fix that? But let me know if there is"). PlantUML has
no setting that places a participant, but a gap between two neighbouring participants is the widest that any
message between them needs, so a hidden message with a long enough label holds a gap open. The probe redraws each part
of scenario 1 through the page's own engine (`window.plantuml.render`), with one row added after the participants:

```
autonumber stop
A -[hidden]> B : <color:transparent>_____…</color>
& B -[hidden]> C : <color:transparent>_…</color>
& C -[hidden]> D : <color:transparent>_____…</color>
& D -[hidden]> E : <color:transparent>_____…</color>
autonumber 38
```

(The participants are written A to E here. `&` puts the four on one row under teoz; the part's own `autonumber` line
comes back after the row.) Each label is sized so that its gap is at least the widest that gap is in any part (699.5,
78.7, 368.1 and 711.6 px), with the underscore's width read from drawings of the participants alone.

| Part | Lifelines as drawn | Lifelines with the row (labels of 648, 8, 312 and 669 underscores) |
|---|---|---|
| 1 | 355.1, 1,054.6, 1,133.3, 1,501.4, 2,213 | 355.1, 1,099.3, 1,177.9, 1,555.2, 2,326.4 |
| 2 | 577, 638.9, 717.5, 841.1, 963 | 25.4, 769.6, 848.2, 1,225.6, 1,996.7 |
| 3 | 433.6, 495.5, 574.2, 942.3, 1,651.9 | 25.4, 769.6, 848.2, 1,225.6, 1,996.7 |

With the row, the gaps are 744.2, 78.6, 377.4 and 771.1 px in all three parts, to 0.1 px. The first lifeline still
stands at 355.1 in part 1 (something drawn left of the first participant) and 25.4 in the others, which the page
evens out by placing each part, not by drawing it again. The row draws nothing: no underscore, and each part has as
many arrowheads (`polygon`: 42, 0 and 105), horizontal lines (39, 4 and 17) and vertical lines (5 each) with the row as
without it. Each part is 39 px taller.

- **Numbers.** Without `autonumber stop` the hidden messages took numbers, and a number is part of a message's label:
  the parts numbered from 38 had gaps 7.5 px wider than part 1's, numbered from 1.
- **How far one label pushes** (`PROBE_CAP=1`): 1.08 px per underscore and linear, from a 139.9 px gap at 100
  underscores to 1,974.4 px at 1,800. At 2,000 the engine drew a 905 x 661 picture with no lifelines, not looked into
  (an error picture, by its look); a wider gap would take a wider character.
- **Width.** The probe's calibration overshot each target (744.2 for 699.5, 771.1 for 711.6), so its aligned diagram is
  about 2,391 px wide against 2,254 for the widest part today (+6%). Labels sized to the targets would make it about
  2,280 px (+1%): the widest left side (355.1), the target gaps (1,857.9) and the widest right side (64.1).
- **Time.** Each part drawn again with the row: 38 to 100 ms (scenario 1, the workers, no cache, as the source
  changed); a drawing of the participants alone, 5 to 7 ms.

```
PLAYWRIGHT_CORE=... node probe/probe-align.js <report> 1 transparent
PROBE_CAP=1 PROBE_CAP_STEPS=1000,1200,1400,1600,1800,2000 PLAYWRIGHT_CORE=... node probe/probe-align.js <report> 1
```

## M8. What R2's second drawing costs on the whole report (probe/probe-twice.js)

Asked by the owner ("that could be a lot if there were say 5 diagrams - what would that mean for a report like this
one"). The report holds 40 diagrams drawn in parts (24 laid out, 16 in views not shown), 105 parts, in 29 scenarios, at
most 4 in one scenario. The page drew on 4 workers. For each diagram, its parts sent to the engine together, a comment
line making every drawing miss the cache, wall-clock from the first send to the last picture:

| | All 40 diagrams | Typical diagram (2 or 3 parts) | Scenario 492 (8 parts) | Scenario 278 (15 parts) |
|---|---|---|---|---|
| Drawn once, as today | 5,952 ms (15,235 ms of worker time) | 67 to 172 ms | 328 ms | 877 ms |
| Drawn again with the row | 6,035 ms (16,895 ms) | 67 to 191 ms | 348 ms | 1,082 ms |
| Parts' widest disagreement on a gap, today | | 29 to 669 px (0 in one diagram) | 669 px | 8 px |
| ... after the second drawing | 0.1 px or less in all 40 | | | |

So the second drawing costs what the first does, and the parts would move into line that long after they appear: a
tenth of a second for most diagrams, a third for the 8-part one and a second for the 15-part one. A scenario's diagrams
share the 4 workers: scenario 48's four 2-part diagrams took 137, 75, 83 and 84 ms again. Opened all at once, the
report's 40 diagrams in parts would take 6 s more of the workers' time, beside the 3 minutes the probe's page took to
draw all 1,552 diagrams. Not measured here: the page's own work on each picture it is given again (note buttons, the
role pass, the zoom), which 5.5.1 made per part.

**An outline instead, rejected.** The whole diagram drawn once with each note cut to its heading, its three longest
lines and its table rows, so that the row could be sized before the parts are drawn and nothing would move: it saved
few `<text>` (notes are already cut to 10 lines by default), it is one picture, so one worker, and it would hold the
first part until it is drawn. It cost 36.6 s over the 40 diagrams, 31,355 ms of that on scenario 278 alone (28,310
`<text>`), and its gaps fell short of a part's in 1 of the 40.

## M9. Does drawing a long diagram in parts save time? (probe/probe-split.js, the 5.5.1 toggle bench)

Asked by the owner ("are we actually gaining any performance improvement by splitting the diagrams into multiple
parts - do the toggle buttons work faster when they're split, even though you still need to redraw up to 10
diagrams?"), with the feature holding scenarios 277 and 278 as the extreme case: 278's diagram is 15 parts, 45,943
`<text>`, 112,940 px tall drawn whole. The owner used the report in a browser during part of probe-split's run; the
rows of that stretch (several seconds for parts and whole alike, against about 100 ms in M8) are left out.

**The engine, whole against parts** (probe-split.js, 4 workers, the median of 2 rounds; parts sent together):

| Diagram | Parts | In parts: wall (workers' sum) | Whole |
|---|---|---|---|
| Scenario 278 | 15 | 1,317 ms (11,190 ms) | 39,089 ms, then refused: `Diagram too large for browser rendering: 1750x112940 (max 98304)` |
| Scenario 492 | 8 | 379 ms (1,979 ms) | 1,528 ms |
| Scenario 398 (42,886 px) | 2 | 575 ms (634 ms) | 806 ms |
| Seven 2-part diagrams (scenarios 49 to 306) | 2 | 69 to 130 ms | 73 to 163 ms |

The engine checks its size cap (`maxSvgSize`, 98,304 px; PlantUML's own default is 8,192) after it has laid the whole
picture out, so 278 cost 39 s before it was refused. The cap is a number the page passes at three sites and was raised
before; raised again (to 1,000,000 in a copy), the picture is drawn, at that cost.

**A toggle, end to end** (the 5.5.1 bench, `float-bench.js`: both scenarios open, Assertions, Steps, Databases and
Setup each switched and back; the report's own 5.4.4 scripts, then 5.5.2's swapped in with `patch-report.js`):

| Page | New pictures on screen | All done | Longest task |
|---|---|---|---|
| As written (5.4.4), in parts | 11,626 to 14,573 ms | 11,875 to 15,000 ms | 10,927 to 13,610 ms |
| 5.5.2, in parts | 699 to 1,126 ms | 1,925 to 2,516 ms | 379 to 548 ms |
| 5.5.2, whole (`_maxDiagramHeight` and the cap raised) | none within the bench's 120 s | 120 s (timed out) | 459 ms; 34 to 45 s of engine time a switch |

(Databases switching on is the quick one in every page, 153 to 350 ms: it barely changes these diagrams.) A CPU profile
of one 5.4.4 toggle: 11.6 of its 13.4 s is `getBBox` under `makeNotesCollapsible` (`getNoteBBox` 10.1 s, the orphan
sweep of `findNoteGroups` 1.5 s), with 977 forced layouts (`UpdateLayoutTree`, 10.2 s): the layout thrash 5.5.1 found
on this same report and fixed. The run of the whole page was stopped after 3 of its 8 switches.

So the parts are what keep a long diagram usable: the engine's time grows far faster than a picture's size, 30 times
for 278 and 4 times for 492, while a 2-part diagram costs about the same either way. The seconds the owner saw on a
toggle were the 5.4.4 scripts, not the parts.

## M10. A compact outline, so the parts are drawn once (probe/probe-compact.js)

Asked by the owner of R2's second drawing ("This sounds too slow, is there any alternatives?"). A gap depends only on
the widest thing drawn across it, so an outline needs the participants and, of the body, only what is widest for each
kind of row: for each message kind (its two participants and its arrow, with the side a note is attached on), the three
widest labels and the three widest attached notes, and the three widest step bars (`hnote across`). Widths are measured
in the page (a canvas, `13px system-ui`, markup stripped). The outline's numbering starts at a number as long as the
diagram's last, since a number is part of a label. These long sources hold no groups: their rows are messages (708
requests and 708 replies in scenarios 1, 278, 492 and 398), notes attached to the message before them (`note left`,
`note right`: 1,330), step bars (978, each with a comment line) and the prefix. The report as written, with 5.5.2's
scripts swapped in (`patch-report.js`), at 1920 x 1000:

| | All 40 diagrams in parts |
|---|---|
| Rows kept | 17 to 60 of 69 to 952 (scenario 278: 17 of 882) |
| Outline drawn in | median 61 ms, 16 to 175 ms but one 510 ms (scenario 278: 52 ms; 492: 35 ms) |
| Parts drawn once with the row sized from it, lined up to 0.1 px | 40 of 40 |
| Outline short of a part's gap as drawn without the row | 1 (scenario 492, by 154.5 px), and its parts lined up all the same |
| The lined-up diagram's width over its widest part today | median 2.1%, max 16.0% (10 of the 40 over 12%) |

Against R2's second drawing (M8): about 3.4 s of the workers' time over the 40 diagrams in place of 6.0 s of wall-clock
(16.9 s of the workers'), no part given a second picture and decorated again, and nothing moves on screen; the first
part waits for its outline (61 ms at the median). The width is the price of lining up at all, whatever sizes the row: a
diagram whose parts are wide in different gaps becomes as wide as all those gaps together.

**Against today** (the owner: "I don't really care about Draw twice (old R2) I care about the baseline"). Each diagram
drawn as today (its parts sent together) and with R2 (its outline, then its parts with the row), in turn, three rounds,
every drawing missing the cache, wall-clock from the first send to the last picture, the median of the three:

| Outline | Outline drawn in | R2 over today, 40 diagrams | Added per diagram | 15 parts | 8 parts | Typical 2 parts |
|---|---|---|---|---|---|---|
| Three widest of each kind, notes whole | median 63 ms (max 300) | 6,641 to 10,105 ms (+52%) | median 64 ms (-82 to +345) | -8% | +4% | +70 to +100% |
| Lean (`PROBE_LEAN=1`): the widest of each kind, a note cut to its first line, its two widest and its table rows | median 18 ms (max 121) | 4,875 to 6,164 ms (+26%) | median 20 ms (-19 to +118) | +1% (780 to 791 ms) | +1% | +16 to +36% (64 to 80 ms at the least) |

The lean outline is short of a part's gap as drawn in 7 of the 40 (the full one in 1), and all 40 still line up to 1 px:
the row moves what a part needs. Its width over the widest part is the same, median 2.1% and max 16.0%. (The two runs
were a few minutes apart, so compare each outline with its own "today".)

## M11. Where a lined-up diagram's width comes from (probe/probe-excess.js, probe-idle.js, probe-notes-left.js)

Asked by the owner on 2026-10-24: "Are we absolutely certain there's no way to improve on this 2-16% situation, maybe
make it 2-7% (and I'm not talking about 'only doing it for some diagrams and not others')". The report as in M10
(5.5.2's scripts), 1920 x 1000, its 40 diagrams in parts.

**The floor.** Lined up, every part has the same gaps between its lifelines, so the parts stacked are one drawing of
the whole diagram, and no row can make them narrower than the engine draws the diagram in one piece. Drawn whole (the
39 diagrams of up to 8 parts; scenario 278's 15 parts are too tall to draw whole, M9):

| The whole diagram over its widest part today | Diagrams |
|---|---|
| 0% | 24 |
| 0.9 to 1.1% | 4 |
| 3.9% | 1 |
| 8.6 to 12.2% | 11 (scenarios 14, 37, 38, 47, 48, 49, 148 in two views, 153, 154, 287) |

A diagram is as wide as its widest part unless its parts are wide in different places: the median is 0 and the most
12.2%. Splitting a diagram gives each part only its own share of the width, so M10's comparison with the widest part
measured what splitting saves as much as what lining up costs.

**Where the rest of M10's 2.1% and 16% came from.** Split against the widest part into the left side (left of the first
lifeline), the span (first lifeline to last) and the right side (right of the last), M10's lined-up drawings were 4,284
px wider over the 40 diagrams: -98 px on the left, 2,736 px in the span and 1,647 px on the right. Against the whole
diagram they were 46 px wider at the median (6 to 95): the row does not make the gaps it is sized for. Sized to the
whole diagram's own gaps, the row over scenario 41's participants alone gives 706.4, 96.8, 377.3 and 711.8 px for
692.1, 78.7, 368.0 and 710.8. M7's calibration (one underscore 1.0791 px over 32.0) was read from the first gap of the
first diagram, and the gap a label starts from differs from one pair to the next. Drawn in a part, the row gives
exactly what it gives over the participants alone (scenarios 41, 44, 80, 231 and 153). Two corrections, measured:

| The row | Lined up, minus the whole | Lined up, over the widest part today | Parts' widest disagreement |
|---|---|---|---|
| M10's (a number as long as the diagram's last; labels from M7's calibration) | median 46 px (6 to 95) | median 2.1%, max 16.0% | 0.1 px |
| Each kept message given its own number in the outline (`autonumber N` before it) | median 41 px (6 to 71) | median 2.1%, max 16.0% | 4.4 px |
| Also the row drawn over the participants alone first, each label lengthened or shortened by its gap's error, up to three times, and a gap the participants alone make wide enough given no label | median 13 px (-16 to +29) | median 1.0%, max 12.2% | 17.0 px (in 24 of the 40 over 1 px) |

The labels of one row are not independent: on one row (`&`) a label's room depends on its neighbours', so the
correction does not settle to the pixel (scenario 153's labels settled 17 px short of its first gap). And with the row
no longer wider than asked, the lean outline's own shortfalls show: M10's row had hidden them by its overshoot. A row
that lines the parts up on one drawing and costs nothing over the floor needs both settled, which is R2's first step
(plan Q7).

**What makes the floor wide.** In the 11 diagrams over 7%, a part other than the widest has a wide reply note: on the
last participant in scenarios 14, 47, 48, 153, 154 and 287 (the widest part's right side is 64 to 109 px, the other
part's 328 to 361), on one in the middle in 37, 38 and 49, and a request's note in 148. A payload note sits in a
column of its own: a request's note left of its arrow (beside the caller, for the test's own calls), a reply's note
right of its arrow (beside the service that replies). Lined up, each column is as wide as its widest note in any part,
and the columns add up.

**Each source changed one way, the parts, the outline and the whole alike** (probe-excess.js, the corrected row;
"today" is the widest part as drawn now):

| Change | Lined up, over today | The whole, over today (the floor) | Height |
|---|---|---|---|
| None | median +1.0%, max +12.2% | median 0%, max 12.2% (11 over 7%) | the same |
| A reply's note on the left of its arrow (`respleft`) | median -3.1%, max +3.9% | median -4.1%, max +3.0% (none over 7%) | the same |
| A request's note on the right of its arrow (`reqright`; M10's row) | median -7.8%, max +19.9% | | the same |
| Notes wrapped at 600 px for 800 (`wrap600`; M10's row) | median +2.0%, max +16.0% | | median +0.9%, max +13.2% |
| A note under its message, across its two participants (`overspan`) | median -21.9%, max -1.7% | median -24.0%, max -13.8% | median +23.7%, max +29.0% |

**A reply's note on the left, on every picture** (probe-notes-left.js; every laid-out sequence picture, whole diagrams
and parts, 546 of the 552 with a reply note): 475 narrower and 58 wider; the width at the median -1.1% (p10 -18.0%,
the most -41.7%; p90 +1.1%, the most +33.8%); the height the same; the level each is drawn at in its box at the median
+1.0% higher (p90 +4.8%, the most +17.9%), and lower for 20 pictures: by more than 5% for 4, by more than 10% for 3
(scenario 288 from 80.9% to 68.6%, 291 from 80.0% to 68.1%, 149 from 100% to 87.8%, all whole diagrams); 201 pictures
fit their box at 100%, 247 with the notes moved. So it takes the worst case off the diagrams in parts and puts one as
large on three ordinary diagrams.

Asked by the owner the same day, whether the loss falls on diagrams that start at 100% and so have room to spare
("Surely it's better if the size reduction is on the 'ordinary' diagrams which would be fully zoomed in to start, as
opposed to the large diagrams that are zoomed out"): mostly not. Of the 201 pictures that start at 100%, 197 stay there
and 4 are drawn smaller (2.4% at the median, 12.2% at the most, scenario 149). Of the 345 that start zoomed out (79.9%
at the median), 315 are drawn larger (1.1% at the median, 17.9% at the most), 14 stay as they are and 16 are drawn
smaller (2.9% at the median, 15.2% at the most): 16 of the 20 losses fall on diagrams already zoomed out, two of them
from about 80% to 68%. The three largest are one case: the test's last call returns a large body (a CSV or JSON payload)
from the service under test, whose note today sits beside that service, in a gap already as wide as the long labels of
the calls that service makes; moved beside the caller, it widens the caller's column by about 400 px.

Lining up itself does spend room where there is room: at 1920 the whole drawings of scenarios 37, 38 and 40 are 3.9 to
9.2% wider than their widest parts and still fit their box at 100%. The 9 that are drawn smaller start at 68 to 100% and
lose 5.5 to 10.9% of it (153 from 98.0% to 87.3%, 287 from 69.0% to 61.9%); with replies' notes on the left the same 9
start at 67.0 to 100% (153 at 99.9%, 287 at 67.0%).

**Left out: idle participants** (probe-idle.js). A part could leave out the participants it has no message for, so that
its notes run on where their lifelines stand, the page drawing those lifelines in itself. Estimated from the gaps as
drawn, it narrows 3 of the 40 (scenarios 1, 148 and 492) and none of the 11 above; not pursued.

```
PROBE_ROWFIX=0 PROBE_VARIANTS=reqright,respleft,wrap600 PLAYWRIGHT_CORE=... node probe/probe-excess.js <report> 1920 1000
PROBE_TRUENUM=1 PROBE_ROWFIX=0 PROBE_VARIANTS=respleft,overspan PLAYWRIGHT_CORE=... node probe/probe-excess.js <report> 1920 1000
PROBE_TRUENUM=1 PROBE_VARIANTS=respleft,overspan PLAYWRIGHT_CORE=... node probe/probe-excess.js <report> 1920 1000
PLAYWRIGHT_CORE=... node probe/probe-idle.js <report> 1920 1000
PLAYWRIGHT_CORE=... node probe/probe-notes-left.js <report> 1920 1000
```

## M12. What PlantUML itself could do (probe/probe-newpage-cost.js, probe-teoz-scaling.js, a Java profile)

Asked by the owner on 2026-10-24 ("are there any plantuml issues/prs we could raise that would help with this?"). Read
at PlantUML master as of that day (`331a6e774` is the last change to `net.sourceforge.plantuml.real`), with the browser
build Kronikol ships (5.5.2's scripts in the report, as M10) and the Java build `1.2026.9beta2` (`70cc513`).

**Teoz already lines pages up.** A sequence diagram cut with `newpage` is laid out once, whole (the X line is solved
in `SequenceDiagramFileMakerTeoz.createMainTile`), and each page is drawn from that layout, clipped to its stretch
(`PlayingSpaceWithParticipants.drawU`): every page has the participants' heads at its top, a footbox at its bottom
unless `hide footbox`, and the whole layout's width. The browser build always draws page 0
(`PlantUMLBrowser.buildSvg` calls `getTextBlock(0, ...)`), and each page lays out the whole diagram:

| Diagram | Parts as today (sent together) | `newpage` before each part, page 0 | Whole at once |
|---|---|---|---|
| Scenario 153 (2 parts, 78 messages) | 70 ms wall, widest 1,827 px | 92 ms, 2,050 px wide | 110 ms, 2,050 px wide |
| Scenario 492 (8 parts, 464 messages) | 235 ms wall (1,293 ms of the workers') | 1,150 ms, 1,824 px wide | 1,334 ms, 1,824 px wide |

Page 0 is exactly as wide as the whole diagram (lined up by the engine), but a page costs nearly the whole diagram's
drawing: the layout and the walk over every tile, not the SVG, is the cost.

**The layout grows faster than the diagram once messages carry notes** (probe-teoz-scaling.js: 5 participants, N
request and reply pairs with short labels, with and without a 6-line note on each reply; each drawing misses the cache):

| Pairs | Browser, teoz, no notes | Browser, teoz, notes | Browser, classic, notes | Java teoz, notes (less ~0.5 s JVM start) |
|---|---|---|---|---|
| 100 | 28 ms | 62 ms | 50 ms | ~0.1 s |
| 200 | 48 ms | 178 ms | 168 ms | ~0.17 s |
| 400 | 88 ms | 1,139 ms | 1,243 ms | ~0.57 s |
| 800 | 186 ms | 21,940 ms | 21,368 ms | ~2.7 s |

Without notes the time is linear (0.22 to 0.42 ms a pair); with them it is about four times as long for twice the
pairs in Java and nineteen times in the browser build. A Java Flight Recorder profile of the 800-pair diagram puts
67.5% of the samples in `real.AbstractReal.getCurrentValue`, under `real.RealDelta.getCurrentValueInternal`, which is
`delegated.getCurrentValue() + diff`: a position defined from another one is recomputed through the whole chain on
every read, nothing cached, and every link also registers its value on its line. `d087fc0c4` (2026-07-15, "migrate Teoz
sequence diagrams to Real-based Y positioning") put every tile's vertical position on such a line, chained from the
tile above it. Whether notes lengthen the chains or only read them more often is for the fix to establish; scenario
278's 39 s whole (M9) is the same shape at Kronikol's scale.

```
PLAYWRIGHT_CORE=... node probe/probe-newpage-cost.js <report> 153,492 1
PLAYWRIGHT_CORE=... node probe/probe-teoz-scaling.js <report> 50,100,200,400,800
java -XX:StartFlightRecording=filename=notes800.jfr,dumponexit=true,settings=profile -jar plantuml.jar -tsvg notes-800.puml
jfr print --events jdk.ExecutionSample --stack-depth 64 notes800.jfr
```

The made-up sources are written by probe-teoz-scaling.js's `source(n, teoz, notes)`; the Java run used the same text
saved to files, less the comment line that makes each browser drawing miss the cache.
