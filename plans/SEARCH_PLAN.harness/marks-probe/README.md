# Marks probe (SEARCH_PLAN 4.6.5), 2026-10-26

How to mark matched words inside a drawn diagram, measured in Chromium 149 (playwright-core 1.61.0 at
`C:/Code/Kronikol4J/playwright`), Firefox 148 and WebKit 26.4 (the playwright-core 1.59.1 inside the Microsoft.Playwright
1.59.0 NuGet package, whose bundled builds the E2E suite uses). Bundled browsers only: no `channel`, no
`executablePath` (CLAUDE.md). Run `probe.js <engine>`, `live.js <engine>`, `check-span.js`; `build-markup.js` captures
two drawn diagrams from the live demo report (5.4.1, payloads JSON and Expanded) into `markup.html`, which every engine
then runs on. The 371 screenshots are not committed; the scripts write them again. Numbers: `summary.txt` (its WebKit
`textLength` rows came from a tspan-based reference and are wrong; the note at its end has the right figures, from
`check-span.js`) and `out/`.

"ok" is within about 2 px of the glyphs.

| | Chromium 149 | Firefox 148 | WebKit 26.4 |
|---|---|---|---|
| `::highlight` on SVG `<text>` | background only | background, color, decoration | paints nothing |
| `::highlight` in the scroll box (HTML) | yes | yes | yes |
| `Range.getClientRects` on SVG text | ok but `spacingAndGlyphs` | wrong unless 1 unit = 1 px (9-79 px) | y always wrong (21-2,098 px) |
| `getExtentOfChar`, first to last character | ok | ok | ok but `spacingAndGlyphs` |
| `getStartPositionOfChar` + `getSubStringLength` | short with `spacing` | short | short with `spacing` |
| `getScreenCTM` to the window | ok | ok | wrong under any CSS transform |
| `<rect>` in the SVG's own coordinates | follows every zoom, transform, viewBox, scroll | same | same |
| HTML overlay | 50-1,236 px off after a zoom | same | same |
| 50 marks, warm / first after a layout | 0.09 / 3.3 ms | 0.08 / 4.4 ms | 0.27 / 5.5 ms |

Taken into the plan: a `<rect pointer-events="none">` per run of marked characters on a row, from
`getExtentOfChar(first)` to `getExtentOfChar(last)` of each `<text>`, merged per row, inserted just before the `<text>` it
marks (the note's shape below it, the words above; both themes), every extent read before any insert (312 rects: 1.4 to
3 ms; a read after each insert forces a layout: 24 / 112 / 261 ms). In the scroll box, `CSS.highlights` (follows the box's
own scroll and zoom, which no rect or overlay does), brought into view with
`scrollTop += (range middle - box middle) / (box rect height / clientHeight)`, after the box's text is built (it is a
placeholder until it nears the window).

Pitfalls: the engine draws one `<text>` per word and per run of spaces, a row being the siblings with one `y`; a pair of
U+200B ends a wrapped row and stands for the space the wrap removed; never split a `<text>` into `<tspan>`s (WebKit lays
out a `textLength` text differently once split, up to 51 px); `.k-step-text` has `fill … !important`; a redraw replaces
the svg, so marks are drawn again.

Found on the way (SEARCH_PLAN B16): WebKit 26.4 on Windows draws a scrollable `<foreignObject>` box unscaled, spilling
out of its card, whenever the svg is drawn at any width other than its viewBox width (a zoom, or a wide diagram shrunk to
fit); `fo-minimal.js` reproduces it in a ten-line page with no report code. `getBoundingClientRect` reports the scaled
box, so a check on geometry alone passes. Safari not checked.
