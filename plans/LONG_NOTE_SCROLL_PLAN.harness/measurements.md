# Measurements (LONG_NOTE_SCROLL_PLAN)

Taken 2026-10-21 on the owner's Windows 11 machine (device scale 1), headless, with the Playwright 1.59.1 driver the E2E
project bundles: Chromium 147 (build 1217), Firefox 148 (build 1511) and WebKit 26.4 (build 2272), each started through
`tools/playwright/installed-browser.js`'s `launchInstalled` (CLAUDE.md), which for these bundled builds is a plain
launch. The reports were written by the published `Kronikol` 5.2.1 package through `ReportGenerator.GenerateHtmlReport`
(`gen/*.cs`), so their page scripts are 5.2.1's; 5.2.2 changed none of the code these measure (the role pass's step
bars with one lifeline, and zoom per part).

**Fixtures.** `gen/gen.cs` and `gen/gendesigned.cs` feed `GenerateHtmlReport` a hand-written PlantUML source: it carries
`!pragma teoz true` and `skinparam wrapWidth 800` as a generated one does, but not the generator's `DiagramLook` prefix
(`skinparam defaultFontName system-ui`, its `<style>` block), so notes paint in the engine's default `sans-serif` 13 px
where a generated note paints `system-ui`. `gen.cs` passes `ResolvedToggleDefaults.BuiltIn` with the Details state
only, so its pages show the YAML view with the headers hidden (the built-in defaults); `gendesigned.cs` sets the JSON
view and the headers shown, so its pages draw the source as written. Each table compares like with like.

Shapes: **mixed** is one test of 40 calls whose responses are JSON of 1,500 lines for 4 calls, 150 for 12 and 12 for 24
(239,276 characters of source); **mixed-capped** (`gen.cs`) cuts every body at 35 JSON lines, drawn as 37 YAML rows with
no `...` and no extra row (the first prototype); **mixed-designed** (`gendesigned.cs`) cuts every body over 40 lines as
the plan designs it: the header line, the blank line, 40 body lines, `...` and one `<color:#FFFFFF00>` row holding the
widest body line (32,524 characters); **one-huge** is one call with a 1,500-line body; **six** and **six-capped** are six
calls (bodies 1,500, 12, 150, 12, 1,500 and 12 lines), which draw as one part once capped.

## M1. What an expanded long note costs today, and capped (probe/probe.js)

`node probe/probe.js <report>` opens every `details`, draws every diagram and waits for the last SVG.

**M1a, the YAML view with headers hidden (`gen.cs`):**

| Report | Parts | Diagram height | `<text>` | Drawn in | `findNoteGroups`, all SVGs |
|---|---|---|---|---|---|
| one-huge, Expanded | 3 | 10,413 + 10,352 + 6,129 px | 11,001 | 1.4 s | 12 ms |
| mixed, Expanded | 17 | about 144,000 px | 58,446 | 3.9 to 5.7 s (three runs) | 220 to 240 ms |
| mixed, Truncated at 10 (the default) | 2 | 6,650 + 1,790 px | 2,916 | 1.5 s | 124 ms |
| mixed-capped, Expanded (first prototype) | 2 | 7,530 + 7,150 px | 5,348 | 1.5 to 2.3 s (two runs) | 113 to 144 ms |

The one-huge note alone is cut by the 15,000-character rule into three diagrams with "..Continued On Next Diagram..".

**M1b, the plan's shape, JSON view with headers shown (`gendesigned.cs`), every payload expanded:**

| Report | Parts | Diagram height | `<text>` | Drawn in, steady | First run (cold) | `findNoteGroups` |
|---|---|---|---|---|---|---|
| mixed-json (today) | 17 | about 129,000 px | 53,854 | 2.54 to 2.59 s (three runs) | 6.75 s | 108 to 129 ms |
| mixed-designed (capped at 40) | **3** | 8,195 + 8,195 + 2,790 px | **6,218** | **1.18 to 1.19 s** (three runs) | 2.64 s | 71 to 78 ms |

Steady runs: 54% less time to draw, 88% fewer `<text>` elements, 3 parts instead of 17.

## M2. The box in the real report, three engines (probe/proto2.js)

`node probe/proto2.js <engine> out/mixed-capped-expanded.html out/mixed.puml out/p2` lays a `<foreignObject>` holding a
`<div style="overflow:auto">` with each capped note's full body over the drawn body (16 boxes; the 1,500-line ones scroll
26,220 px), hides the engine's body rows (`visibility: hidden`), then checks each behaviour.

| Check | Chromium | Firefox | WebKit |
|---|---|---|---|
| Make 16 boxes, `findNoteGroups` included | 250 ms | 187 ms | 250 ms |
| Box's place in the note (left, top, width, height as fractions of the note) at zoom 1, 2 and 0.5 | identical at all three | identical (to 0.0001) | identical |
| First wheel (240 px) over the box | box 240, page 0 | box 240, page 0 | box 240, page 0 |
| Wheel at the box's end, `overscroll-behavior: auto` | page 240 | page 0 (wheel transaction) | page 240 |
| Wheel at the box's end, `overscroll-behavior: contain` | page 0 | page 0 | **page 240 (ignored)** |
| `window.find()` for text 70% down a box | found, box not scrolled | found, box not scrolled | found, **box scrolled to it** |
| Selection over 3 lines in the box, `toString()` | 2 line breaks of 2 | 2 of 2 | 2 of 2 |
| `focus()` without a tabindex | focusable (tabIndex -1) | focusable | **not focusable**; focusable with `tabindex=0` |
| ArrowDown / PageDown on the focused box, as shipped | 0 / 471 | 0 / 0 | 0 / 431 |
| Note buttons shown, pointer over the box / over the header row | 0 / 4 | 0 / 4 | 0 / 4 |
| Right-click on the box | menu with "Copy box text" (note found) | same | same |
| All 16 boxes scrolled every frame, 60 frames | 16.7 ms average, 17 max | 30 Hz (the headless frame rate) | 15.7 average |

ArrowDown is 0 everywhere because `report-keyboard-navigation-function.js:16-31` takes ArrowUp/ArrowDown for scenario
navigation; see M8.

## M3. Zoom and pan in a narrow window (probe/proto3.js)

`node probe/proto3.js <engine> out/six-capped-expanded.html out/six.puml`, window 640 px, so the diagram (752 px) is
wider than its column and the page offers its slider (fit 76%).

| Check | Chromium | Firefox | WebKit |
|---|---|---|---|
| Ctrl+wheel up over the box | diagram 540 to 579 px, box not scrolled | 540 to 572, box not scrolled | 540 to 579, box not scrolled |
| Slider to 100% (`diagram-natural-size`), then a 120 px drag inside the box | **diagram panned 120 px, nothing selected** | same | same |

(A first run on the two-part mixed report drove the slider of another part: the split-diagram zoom defect 5.2.2 fixed.)

## M4. The box's own cost at scale (probe/scale.js)

`node probe/scale.js <engine> <pre-wrap|pre> <force|lazy|cv|io>` draws a bare SVG of N note rectangles 640 px apart, each
with a box of L JSON lines (box 684 x 574 px, 13 px text, 15 px lines), in a 1,000 px window. **force**: create, fill
and read the first box's `scrollHeight` (a forced layout), timed. **lazy**: fill without forcing; the first two frames
pay. **cv**: lazy plus `content-visibility: auto` on each box. **io**: each box gets its text only when it comes within
one window height of the window (an `IntersectionObserver`, `rootMargin: '100% 0px'`), the plan's design; timed over
four frames.

| N boxes x L lines | force: Chromium | Firefox | WebKit | cv, first paint: Chromium | Firefox | WebKit | **io, first paint (4 frames): Chromium** | **Firefox** | **WebKit** |
|---|---|---|---|---|---|---|---|---|---|
| 16 x 150 | 13 ms | 12 | 14 | | | | 67 | 92 | 94 |
| 16 x 1,500 | 79 | 58 | 70 | | | | 67 | 124 | 108 |
| 100 x 150 | 49 | 38 | 48 | 32 | 87 | 30 | 66 | 121 | 94 |
| 100 x 1,500 | 546 | 337 | 449 | **32** | **730** | 46 | 66 | 92 | 105 |
| 400 x 150 | 194 | 140 | 180 | 32 | 300 | 60 | 61 | 114 | 104 |
| 1 x 10,000 | 35 | 30 | 44 | 50 | 64 | 63 | | | |
| 1 x 50,000 | 163 | 128 | 191 | 200 | 279 | 417 | | | |
| 1 x 200,000 | 676 | 505 | 822 | 832 | 1,147 | 1,957 | | | |

- **io** filled 4 boxes in every row (those within a window height), and its first paint is the four frames it waited
  (about 67 ms at 60 Hz, 120 ms at Firefox's headless 30 Hz) whatever the number of boxes.
- **cv** helps Chromium and WebKit and not Firefox, where 100 x 1,500 took 730 ms against 337 forced.
- Scrolling the two boxes on the screen every frame (60 frames) held the frame rate in every row: Chromium 16.7 ms,
  WebKit about 15.7 ms with occasional 30 to 46 ms frames, Firefox its headless 30 Hz.
- `white-space: pre` instead of `pre-wrap` (Chromium, force): 400 x 150 128 ms, 1 x 200,000 435 ms.
- One box pays for its own text when it is filled. The single boxes of the force column, which is how the plan fills a
  box (no `content-visibility`), give 3.3 to 3.5 microseconds a line in Chromium, 2.5 to 3.0 in Firefox and 3.8 to 4.4
  in WebKit, so 3,973 lines (M9's largest payload) take 10 to 18 ms; under cv it is 4.0 to 5.0 in Chromium, 5.6 to 6.4
  in Firefox and 6.3 to 9.8 in WebKit. `performance.memory` moved too little to report.

## M5. What the engine offers to keep a capped note's width (probe/enginewidth.js)

`node probe/enginewidth.js <report>` renders with the report's own engine and measures the note body:

| Note | Width | Height | Painted rows |
|---|---|---|---|
| 4 narrow lines | 62 px | 70 | 4 |
| + one long line (about 140 characters) | 793 | 85 | 5 |
| + the same line as `<color:#FFFFFF00>` | **793** | **85** | **4** (the line paints nothing) |
| `note<<capw>>` with `.capw { MinimumWidth 600 }` | 621 | 70 | 4 |
| `MinimumWidth 600` + `MaximumWidth 600` + the long line | 621 | 100 | 6 |

A fully transparent line keeps the note as wide (and one row taller) as the line would make it and draws nothing.

## M6. PNG export of an SVG holding a box (probe/taint.js)

Serialize, `<img>` from a data URL, canvas `drawImage`, `toDataURL`: **all three engines export** (no taint) and paint
the box's HTML text (375, 401 and 377 dark pixels in the box for Chromium, Firefox and WebKit).

## M7. Wheel latching (probe/latch.js)

The pointer held still while the page scrolls a box under it: 40 wheel notches of 100 px, 16 or 60 ms apart:

| | Page moved | Box moved |
|---|---|---|
| Chromium | 1,400 | 2,056 (to its end) |
| Firefox | 4,000 | 0 |
| WebKit | 1,400 | 2,056 |

The same at 16 and 60 ms. Firefox keeps a wheel transaction on the page; Chromium and WebKit hand each synthetic notch
to the element under the pointer. Synthetic events carry no gesture phases, so this does not show what a real mouse or
trackpad does in Chromium and Safari: a manual check (plan section 7.5).

## M8. Keys on a focused box (probe/kbd.js)

`tabindex=0` on the box and a keydown listener on it that stops ArrowUp, ArrowDown, PageUp, PageDown, Home, End and Space
from bubbling to the document's scenario navigation (the capture-phase arrow handlers in `report-tabs-script.js` and
`report-features-tools-script.js` still ran, and did nothing here: the page was on the Features tab's Full view):

| | ArrowDown | PageDown | ArrowUp | PageUp | End | Home | Scenario focus moved |
|---|---|---|---|---|---|---|---|
| Chromium | 40 px | 471 | -40 | -471 | to the end | to the top | no |
| Firefox | 45 | 509 | -45 | -509 | to the end | to the top | no |
| WebKit | 40 | 431 | -40 | -431 | to the end | to the top | no |

## M9. A real consumer's notes (probe/notesizes.js)

`node probe/notesizes.js <TestRunReport.html> 40` reads the embedded diagram sources and prints aggregates only. The
split figures are an **estimate**: the script packs the source's units as the page's splitter does (12,000 px, 45 px per
arrow and per note, 18 per note line, a forced cut per extra 15,000 characters), on the JSON lines the source holds (the
page shows YAML by default), without modelling `loop`/`alt` blocks. On BreakfastProvider's ReqNRoll lane report of
2026-10-18 (written by Kronikol 4.26.4; 215 diagrams, 1,717 payload notes):

| | |
|---|---|
| Body lines per note (after its header lines) | median 5, 90th percentile 18, 99th 258, largest 3,973 |
| Characters per note | median 163, 90th percentile 551, 99th 9,450, largest 91,991 |
| Notes over 10 / 20 / 40 / 60 / 100 / 300 body lines | 473 / 143 / **70** / 32 / 28 / 17 (all 70 over 40 are responses) |
| Notes over 15,000 characters | 16 |
| Every note expanded, estimated | **15** diagrams split, 268 parts for 215 diagrams |
| The same, capped at 40 body lines (plus the plan's two rows), estimated | **4** diagrams split, 219 parts |

## M10. Payloads -> Truncated leaves a YAML note whole (probe/repro-yaml.js)

`gen/genyaml.cs` writes a response of 4 JSON lines whose string holds 14 line breaks; its YAML view (the default format)
is 18 lines. `node probe/repro-yaml.js out/yaml-longer.html`:

| Step | Drawn rows of the note | Ends with "..." |
|---|---|---|
| Page load (Truncated, 10 lines) | 11 | yes |
| Payloads -> Expanded | 18 | no |
| Payloads -> Truncated | **18** | **no** (should be 11 and yes) |

Plan section 6, F1.
