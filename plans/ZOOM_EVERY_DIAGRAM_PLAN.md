# ZOOM_EVERY_DIAGRAM_PLAN: every diagram zooms, and one that fits zooms out

**Status: executed as 5.7.0 on 2026-10-24** (the owner: "Can you implement the plan in full as a minor release"): R0, R1
and R2 in one minor release; what the work changed from the plan is in section 9. Written 2026-10-23 at the owner's ask
(section 1), and revised the same day with the owner's answer to Q2 (one scale for the parts of a diagram, Z8) and what
that answer was measured against (M6, B4, Q7), then with what lining the parts up would cost and whether the parts pay
at all (M7 to M10, the owner's questions of the same evening), and last with the owner's decisions and two more asks of
that evening: the floor is 50% (Q1), R2 lines the parts up (Q7), a lined-up diagram starts at its fit level (Q8), the
lines the page adds to draw a diagram never reach a source the reader copies or opens (Z12), and a diagram in parts acts
as one diagram (Z13, Q10, Q11); then on 2026-10-24 with the owner's answers to Q10 and Q11 and their question whether
lining up could cost less (M11, Q12), which they then settled as recommended, and what PlantUML itself could
change (M12, Q13). Every `file:line` is at `origin/main` `b3898519` (5.5.2) under `src/Kronikol/`
unless it says otherwise; CM, CN, IS, RS and RC are `Reports/context-menu-script.js`,
`Reports/collapsible-notes-script.js`, `Reports/inline-svg-styles.css`, `Reports/plantuml-browser-render-script.js` and
`Reports/report-components-script.js`. 5.5.3 (`a3413569`) and 5.5.4 (`9b4d2d64`) came out while this was written: 5.5.3
makes `naturalWidthOf` (CM L1011) read 0 for a picture with no box and wraps `decorateNow` (CN L3442) in a `try`, and
5.5.4 changes the deep search only; this plan keeps both and goes on top of them. The harness is
`ZOOM_EVERY_DIAGRAM_PLAN.harness/` (Appendix A): the probes, and `measurements.md`, cited as M1 to M11.

## 0. In short

- **The ask.** Every diagram has a zoom; a diagram that starts at full size can only zoom out.
- **Why the page zooms today.** A diagram gets a zoom slider only when its picture is wider than its box (CM L1147).
  Ctrl with the wheel, and Ctrl with + or -, act on a diagram only through its own slider (CM L1245, L1226). Over a
  diagram without one the handler returns without preventing the event, and the browser zooms the whole page.
- **How often (M1).** On the consumer report the owner sent on 2026-10-23 (512 scenarios), 217 of the 552 drawn
  sequence boxes have no slider at a 1920 x 1000 window (39%), and 57 of 552 at 1366 x 768 (10%). The owner's case is
  mostly one diagram drawn in parts: 15 of its 24 diagrams in parts have a wide part beside parts that fit, so one
  diagram is drawn at 54.8%, 100% and 72% part by part, and Ctrl with the wheel zooms the first and third parts and the
  page over the second (M2).
- **What changes (R1).** Every drawn diagram gets a slider. A wide diagram keeps today's range (its fit level to 100%)
  and starts at its fit level. A diagram that fits gets a range from a floor of 50% (the owner's choice, Q1) to 100%
  and starts at 100%. Over any drawn
  diagram, Ctrl with the wheel, and Ctrl with + or - on the selected one, zoom that diagram and never the page, at the
  ends of the range too. A diagram zoomed out stays centred in its box, does not pan, and its text can be selected. A
  diagram drawn in parts has one scale and one zoom for all its parts (the owner's answer to Q2): its fit level is the
  smallest of its parts', and its parts zoom and pan together.
- **One diagram, not several (R1, Z12, Z13).** Asked by the owner once one scale was settled. A click on any part
  selects the whole diagram, and its ring goes round every part (today it goes round the part clicked). Copy, Save
  and Open as PNG or SVG give the whole diagram (today the part under the click; a PNG of a diagram too long for one
  canvas is Q10). The diagram ends once: every part but the last is drawn without its footbox (Q11). The lines the page
  adds to draw a diagram (R2's spacer row, the hidden footbox) go only to the engine, so the menu's Copy and Open
  source items, and the source a copied picture carries, never hold them. The source items, Copy Highlighted Text,
  the caller payloads, the numbering, the search and the keys already act on the whole diagram.
- **Bugs found on the way, measured (R0).** The fit is measured against the box with its padding, so 10 pictures drawn
  slightly scaled down (99.3% at the least) have no slider, and a wide picture's slider starts above the level it is
  drawn at (M5). One notch of
  the wheel moves the point under the pointer 91 px down the page (M4): the zoom keeps the point across only. On a
  part, the first notch loses it across too (6.6 to 24.4 px), as the part is made a scroll box only after its scroll
  is set. And the slider of a tall box never follows the page (B4, M6): 1,500 px into a part, its slider is 1,472 px
  above the window, as the box's horizontal scroll makes it the sticky slider's scroll box.
- **What one scale per diagram costs (M6).** Parts that fit today are drawn smaller: 25 of 68 parts at 1920 (by 19.6
  points at the median) and 42 at 1366 (15.4, up to 52). A first drawing whose later part is wider than those above it
  shrinks the parts already shown once (2 diagrams at 1920, 6 at 1366). Resizing every part on each notch costs 191 ms
  on the report's 15-part diagram against 14 ms for one part, so the parts near the window are resized at once and the
  rest just after. And the participants still stand at different places in each part, which one scale does not change; a
  row of hidden messages in each part, sized from a compact outline drawn first (it adds a median 20 ms against today),
  lines them up on one drawing, measured on all 40 of the report's diagrams in parts (Q7, M7, M10). None of these is a
  reason not to; the first two are the price of one text size.
- **Why diagrams stay in parts (M9).** Asked by the owner: drawn whole, the report's 15-part diagram (112,940 px) costs
  the engine 39 s, and its toggles did not finish within 2 minutes, against about 1 s to the new pictures and 2 s to
  done in parts on 5.5.2's scripts; an 8-part diagram costs 1.5 s whole against 0.4 s in parts; 2 parts cost about the
  same either way. The 12 to 15 s toggles the owner saw there were the report's own 5.4.4 scripts (the layout thrash
  5.5.1 fixed), not the parts. That feature (scenarios 277 and 278) is this plan's stress case (7.4).
- **Releases.** R0, a patch, fixes the four bugs. R1, a minor (a new report control), does the ask with the owner's
  floor of 50% (Q1), and makes a diagram in parts act as one. R2, a minor (a diagram feature), lines up the parts of a
  diagram (Q7); the owner took it on 2026-10-23, with a lined-up diagram starting at its fit level (Q8).
- **Questions (section 8).** Q1 answered (50%); Q2 answered (one scale per diagram); Q3 wide diagrams below their fit;
  Q4 a level readout; Q5 Safari's pinch; Q6 the page zoom given up over diagrams; Q7 answered (R2 is in); Q8 answered
  (start at the fit level); Q9 not taken; Q10 answered (a PNG at the largest scale that fits, else the part clicked);
  Q11 answered (one footbox, at the end); Q12 answered (R2 with its row exact, no note moved); Q13 the PlantUML
  changes that would help (not filed).

## 1. The ask

The owner, 2026-10-23:

> It's a bit confusing when you have one diagram after another and the second diagram has no zoom ability because it's
> already zoomed into full and you try and change the zoom and it zooms the whole page. Maybe all diagrams should have
> a zoom function, but the ones who start at full zoom can only zoom out. Then there would be consistency. Can we
> investigate and create a plan.

The owner's decisions and later asks, the same evening, after Q1, Q7 and committing this plan were put to them:

> 1) yes to all three, and also 'start at fit' is better 2) Can we make sure the right click open in new tab as
> plantuml option doesn't include the spacers 3) When we do this zoom all diagrams at once thing, can we make sure it
> looks like we're selecting all the diagrams rather than just one, as we're essentially having them all behave as a
> single diagram now. Likewise is there anything else that is acting like multiple diagrams now that should be made to
> behave like a single diagram now that we're using this pattern?

"All three" are the floor at 50% (Q1), R2 (Q7) and committing the plan; "start at fit" answers Q8, and Q9 was not
taken. The second ask is Z12, the third Z13 with Q10 and Q11.

## 2. What happens today

### 2.1 Which boxes get a slider

- A box is a diagram's container (`[data-diagram-type]`), or each drawn part (`.puml-fragment`) of a diagram drawn in
  parts, which zooms on its own since 5.2.2 (`zoomBoxOf`, CM L25).
- `addZoomButton` (CM L1129) is called for a box once its picture is drawn: by an `IntersectionObserver` over every
  `[data-diagram-type]` with a 200 px margin (CM L1344 to L1371), after a redraw (CN L2738, L2817, L2853, and
  `decorateNow`, CN L3448), after a first drawing (RS L1385), and after a report component's redraw (RC L146).
- A box whose picture is no wider than `clientWidth + 1` (`isDiagramZoomable`, CM L1032) gets no slider, and gives back
  a zoom it was given (`dropZoom`, CM L1148, 5.2.5). A box not laid out measures nothing and is asked again when shown.
- The slider runs from the fit level to 100 (`getFitPercent`, CM L1039: `clientWidth` over the natural width, rounded,
  1 to 100) and starts at the fit level (CM L1160 to L1162).
- A box with no picture (a flame chart, a call tree) gets none: `getSvg` finds nothing.
- The controls are hidden at 768 px and below (IS L77); the wheel and keys still act there.

### 2.2 The wheel and the keys

- **Ctrl with the wheel** (CM L1241): the box under the pointer; no slider of its own, and the handler returns before
  `preventDefault` (CM L1246), so the browser zooms the page. With a slider the event is prevented (CM L1248), at 100%
  too.
- **Ctrl with + or -** (CM L1216): the selected box. A click selects any diagram (CM L986), one that fits included, so a
  reader can select a diagram that fits, see its glow, press Ctrl and +, and get the page zoomed (CM L1227).
- A step is 5% of the slider's range, at least 1 (CM L1232, L1251).
- A trackpad pinch arrives as Ctrl with the wheel in Chromium and Firefox, so the same split applies to pinching.

### 2.3 A zoomed box

- `applyZoomLevel` (CM L1055) sets the picture's width; above the fit level the box gets `diagram-natural-size`, a
  horizontal scroll and the grab cursor (CM L1104 to L1116), and pans when dragged (CM L1259).
- It keeps the point under the pointer across by setting the box's scroll (CM L1069 to L1088), and nothing down the
  page.
- A diagram drawn again keeps its level and its place (5.2.3): `window._zoomState` (CM L1314) with CN's `zoomsBefore`,
  `carryPartZoom` and `swapPicture` (CN L2637 to L2665), the decorate path's restore (CN L3469) and RC L140 to L142. A
  picture drawn again to fit its box takes no zoom (5.2.5).
- A picture narrower than its box stands in the middle of it (IS L140, 5.0.0).

### 2.4 What pins today's rule

- E2E: `DiagramZoomTests.Non_zoomable_diagram_has_no_zoom_slider` (L219),
  `GradualZoomTests.Zoom_slider_does_not_appear_on_non_zoomable_diagram` (L111), and
  `ZoomKeptOnRedrawTests.A_diagram_drawn_again_to_fit_its_box_takes_no_zoom` (L234, its slider assertion L259).
- Core: `DiagramContextMenuTests.ZoomButton_not_added_when_diagram_fits_container` (L899) and
  `ZoomControls_skipped_for_non_zoomable_diagrams` (L909) hold the script's text of the gate.
- 48 reads of the page's first slider in 5 E2E files (`DiagramZoomTests` 12, `GradualZoomTests` 28,
  `ZoomNoteInteractionTests` 5, `ZoomKeptOnRedrawTests` 2, `MobileResponsiveTests` 1) take the first slider in the page
  as the slider of the diagram the test acts on. That holds while only wide diagrams have one; with a slider on every
  diagram, the first can be another diagram's. `GenerateReport`'s fixture draws two diagrams.
- The wiki, `PlantUML-Browser-Rendering.md` at wiki `20688b4`: L160 ("A part that is wider than the page zooms on its
  own") and L175 ("Since 5.2.5 a diagram or a part drawn again to fit its box ... takes no zoom"). No page describes
  zoom as a whole.

## 3. Measured

All on the consumer report, written by 5.4.4 (`measurements.md` has the method and every table).

- **M1, which boxes fit.** 552 laid-out boxes, all sequence diagrams. At 1920 x 1000: 335 wide, 217 fit. At 1366 x 768:
  495 wide, 57 fit. Wide pictures are drawn at a median of 81% (smallest 62%) at 1920 and 58% (smallest 43%, p10 49%)
  at 1366. 15 of the 24 diagrams in parts mix wide and fitting parts at 1920, 13 at 1366.
- **M2, the wheel.** Scenario 1's diagram in three parts: Ctrl with the wheel over a fitting part is not prevented (the
  page zooms), over a wide part it zooms the part.
- **M3, text and height.** Messages are drawn in 13 px text and notes in 11 px, so they paint at 6.5 and 5.5 px at 50%,
  and 3.3 and 2.8 px at 25%. 171 of the 217 fitting pictures at 1920 are taller than the window (median 1,777 px); a
  floor of 50% would let 82 of them be seen whole, 25% 115. At 1366: 31 of 57, 21 whole at 50%, 27 at 25%.
- **M4, the point under the pointer.** One notch on a whole wide diagram (2,680 x 5,900, 66.8% to 70%): -1 px across,
  91.2 px down. On a part's first notch: 6.6 px across and 11.7 px down at 1920, 24.4 and 42.2 px at 1366.
- **M5, the padding band.** 10 boxes at 1920 and 2 at 1366 are drawn scaled down (to 99.3% and 98.1% at least) with no
  slider; scenario 10's slider reads 68% at its lowest while its picture is drawn at 66.8%.
- **M6, one scale for a diagram's parts.** The table of parts drawn smaller and of drops during a first drawing (in the
  bullet "What one scale per diagram costs" of section 0); scenario 1's lifelines at 355 to 2,213 in its first part, 577
  to 963 in its second and 434 to 1,652 in its third; a notch costing 17, 62 and 191 ms for every part of a 3-, 8- and
  15-part diagram against 4, 10 and 14 ms for one part; the slider 1,472 px above the window 1,500 px into a part.

## 4. Design

### 4.1 Rules

- **Z1, every drawn diagram zooms.** Every diagram with a drawn picture gets a slider once it is laid out, one for the
  whole diagram when it is drawn in parts (Z8). This covers the component diagram page
  (`ComponentDiagramZoomTests`) and, as every picture the render queue draws is handed to `addZoomButton` (RS L1385),
  the internal-flow popup's activity diagrams, where no test covers zoom today (fact 11 of 6.2 is the first). A box
  with no picture gets none, as today, and a box not laid out gets its slider when shown (5.5.3).
- **Z2, two ranges.** Measured against the box's content width (Z3):

  | The picture is | Range | Starts at |
  |---|---|---|
  | wider than the content width ("wide") | its fit level to 100% | its fit level (as today) |
  | no wider ("fits") | the floor to 100% | 100% |

  The floor is one constant for every diagram, 50%, the owner's choice (Q1). It is not an option.
- **Z3, the content width.** "Wide" and the fit level are measured against the box's width less its padding, the
  width a picture is drawn in (`max-width: 100%`, IS L4). This is R0's B1.
- **Z4, the wheel and keys are the diagram's.** Over a box with a slider, which is now every drawn diagram, Ctrl with
  the wheel zooms that box and the event is prevented, at either end of its range too, as a wide box at 100% does
  today. Ctrl with + or - zooms the selected box the same way. Outside a diagram, over a picture still being drawn, and
  with no diagram selected, they stay the browser's. The step stays 5% of the range, at least 1: with a floor of 50, a
  picture that fits moves 3 points a notch, 17 notches from 100% to 50%.
- **Z5, zoomed out.** A picture that fits, below 100%, is drawn at its natural size times the level, its height in
  proportion, and stands in the middle of its box as it does at 100% (IS L140). It has no scroll, no grab cursor and
  no pan, and its text can be selected. Its box is marked `diagram-zoomed-out`, a class with no style, kept for the
  state and the tests. `diagram-natural-size` keeps its meaning: zoomed in past the fit level.
- **Z6, the point under the pointer.** A zoom given a pointer (the wheel, and the keys and slider, which use the last
  pointer position over the box as today) keeps the picture's point under the pointer in place down the page, by
  scrolling the window by the distance the point moved. Across, a wide picture keeps it by the box's scroll, as today
  (for a part from its first step, B3); a picture that fits shrinks towards the middle of its box, so across it moves.
  The down-the-page half applies to wide pictures too: it is R0's B2. On a diagram in parts (Z8) the point is taken in
  the part under the pointer, and kept across by the container's scroll.
- **Z7, a redraw keeps the zoom in its direction.** A box drawn again (5.2.3) keeps a zoomed-in level only if its new
  picture is still wide, and a zoomed-out level only if its new picture still fits, either one moved into the new
  range. Otherwise it starts where a new box starts: a wide picture at its fit level, one that fits at 100%. So a wide
  diagram zoomed in and drawn again to fit comes back at full size, never smaller (5.2.5's rule kept), and a fitting
  diagram zoomed out and drawn again wider comes back at its fit level.
- **Z8, a diagram in parts has one scale** (the owner's answer to Q2). Its parts are drawn at one level and zoom
  together, by one slider and one zoom state kept on the diagram's container. The diagram is wide when any part is
  wider than the content width, and its fit level is then the smallest of its parts' (its widest part fits); it fits
  when every part does. A part narrower than the others stands in the middle of the box at the shared level. The parts
  pan together: the container scrolls across (it already can, IS L9) and the parts get no scroll of their own. A click
  selects the diagram, not a part. This replaces 5.2.2's zoom per part, which came from every zoom going to the first
  part's picture; with one zoom, a zoom over any part still keeps that part's point under the pointer (Z6).
  - On a first drawing the parts appear one by one (RS L1134 to L1142), so a part wider than every part above it lowers
    the diagram's level and the parts already shown shrink once, the window scroll keeping the reader's place (M6: 2
    diagrams at 1920, 6 at 1366). A redraw shows its parts only once all are drawn (CN L2718 to L2727), so it never
    does.
  - A notch resizes at once the parts within a window's height of the screen, and the rest in the frames after, one
    part a frame. A part above the window keeps the reader's place by the window scroll: its new height is its natural
    height times the level, known without a layout. Every part at once costs 191 ms on a 15-part diagram (M6).
- **Z9, copies.** Copy and Save as PNG or SVG of a zoomed-out picture give the natural size, as they do for a zoomed-in
  one: the image is sized from the picture's `viewBox` (CM L229). For a diagram in parts they give the whole diagram
  (Z13).
- **Z10, the slider stays in reach.** The slider keeps its look (shown on hover and while selected) and its place at
  the box's top left, and follows the window down the box while the box is on screen, as its `sticky` style meant to
  (B4); with one slider for a diagram in parts that matters more, as the box is the whole diagram.
- **Z11, unchanged.** The controls hidden at 768 px and below, selecting a whole diagram, a zoomed-in picture's pan, the
  keys left to the browser when the selected box is taken off the page (5.2.5).
- **Z12, the lines the page adds stay the page's** (the owner's second ask, section 1). To draw a diagram in parts the
  page adds lines the diagram's author never wrote: `hide footbox` on every part but the last (Z13, Q11) and, with R2,
  the spacer row (`autonumber stop`, the hidden messages, the part's own `autonumber` again; Q7). They go only into the
  text handed to the engine, the render queue item's `source`, and never into a stored source: not the container's
  `data-plantuml`, `_fullSource` or `_noteOriginalSource`, not a part's `data-plantuml` (RS L1139, and the parts of a
  "too large" retry, RS L1427), not `_drawnSource` (CN L2586), nothing the search reads. So everything that reads a
  source sees the diagram as written:
  - the menu's Copy and Open source items, full and current (CM L814 to L825, L877 to L888), which read the
    container's source (`getSource`, CM L573), as since 5.4.0 they leave out a long payload's cap rows (CM L808 to
    L810);
  - the current note text, read from the part's `data-plantuml` (CM L705), and the role pass's reading of a picture's
    notes and fills (RS L2070 to L2076);
  - the redraw's check that a diagram is already drawn from its source (CN L2599, RS L1378 to L1379).

  The engine writes the source it was given into each picture as `<?plantuml-src ...?>`, and a copied picture carries
  it (CM L139): a part's picture holds its added lines. So a copy of a diagram in parts leaves out every part's
  instruction and carries one for the diagram's own source (Z13). A diagram drawn whole gets no added line and keeps
  the engine's instruction.
- **Z13, a diagram in parts acts as one diagram** (the owner's third ask, section 1). Everything a reader does to a
  diagram acts on all of a diagram in parts:
  - **Selected as one.** A click on any part selects the diagram: `diagram-selected`, and with it the ring (IS L39 to
    L42), goes on the container, round every part, and never on a part. Today a click selects the part under it
    (`zoomBoxOf`, CM L25, since 5.2.2), so the ring goes round that part alone and reads as one diagram of several.
  - **One slider** (Z8, Z10), shown on hover anywhere over the diagram; the rule that shows a part's own (IS L59) goes.
  - **Copied as one.** Copy, Save and Open as PNG or SVG (CM L788 to L872) give the whole diagram: one SVG holding every
    part's picture in order, each placed across as the page places it at 100%, each part's ids made its own (two parts'
    pictures can use the same id), and one `<?plantuml-src ...?>` for the diagram's own source (Z12), encoded by the
    page as PlantUML encodes a source (raw deflate by `CompressionStream`, then PlantUML's 64 characters), or none in a
    browser without `CompressionStream`. Today these items act on the part under the click (CM L566 to L567), so a
    reader who saves "the diagram" saves one part of it. Until every part is drawn the picture items are left out, as
    they are for a box with no picture. A PNG is drawn at twice the natural size (CM L208 to L223) and a browser draws
    a canvas of about 32,767 px a side at most; on the report 4 of the 40 diagrams in parts are taller than that at
    twice their size, and 3 at their own size (the longest 115,572 px). So a PNG is drawn at the largest scale up to 2
    that fits, and a diagram too long even at its own size gives the part under the click, its PNG items saying so
    ("Copy part 3 of 15 as PNG"): Q10. Today no check is needed, as a part is cut at 12,000 px (RS L668).
  - **Ends once.** Each part is drawn as a diagram of its own, so each ends with its footbox (the participants drawn
    again under the last message, as no generated sequence diagram hides it) and the next part starts with its heads:
    at every seam one diagram seems to end and another to begin. Every part but the last is drawn with `hide footbox`
    (Z12), so the diagram ends once, at its end, and each part keeps its heads, which name the lifelines below them
    (Q11).
  - **One placeholder.** On a first drawing every part not yet drawn shows its own "Rendering diagram…" box (IS L20 to
    L38), so a 15-part diagram opens as a column of grey boxes. Only the first part not yet drawn shows it, and the
    rest take no height until drawn.
  - **Already one.** The source items read the container's source (CM L573), as does "Copy all caller request
    payloads" (CM L929 to L937). Copy Highlighted Text reads a selection across parts in order, a new row starting in
    another picture (`selectedSvgText`, CM L443 to L449). The numbering runs on across the parts (RS L1101). The search
    and the keyboard routes have no code for parts. A note's buttons act on that note, which belongs to one part, and
    draw the whole diagram again (CN L2690 to L2745).

### 4.2 The state a redraw carries

- `box._zoomPercent` holds the level in both directions (today only above the fit level).
- `window._zoomState.of(box)` returns `{ zoomed, out, percent, scrollLeft, selected }`; `out` is new. A box zoomed
  neither way still returns `{ zoomed: false, out: false, selected }` and reads no layout (5.2.5).
- `give(box, state)` marks the box with its direction and level; `addZoomButton` and `restoreZoomState` apply the mark
  only in its own direction (Z7) and drop it otherwise.
- A diagram in parts keeps its state on its container (Z8), as a whole diagram does, so a redraw that turns a whole
  diagram into parts or parts into a whole keeps it as it is. `zoomsBefore` and `carryPartZoom` (CN L2637 to L2657),
  which hand each part the zoom and selection of the part in its place, give way to the container's own `of` and
  `give`; `swapPicture` (CN L2661) and RC L140 to L142 pass the state whole already.
- The decorate path restores a marked box's picture (CN L3469): also for `diagram-zoomed-out`, and for a diagram in
  parts every part's picture.

### 4.3 Code

CM, in the zoom helpers (CM L1005 to L1212):

- `contentWidthOf(box)`, new: `clientWidth` less the box's left and right padding (a computed-style read beside the
  `clientWidth` read the rule makes today).
- `regimeOf(box)` in place of `isDiagramZoomable`: `none` (no picture, not laid out, or a natural width of 0, as 5.5.3
  reads a picture with no box), `wide` or `fits`.
- `getFitPercent` against the content width; `rangeOf(box)` gives the slider's lowest level and its start.
- `addZoomButton`: a slider for `wide` and `fits`, with `rangeOf`'s range and start (or the level carried in, Z7);
  `none` as today. For a container with parts, one slider on the container, from every drawn part's width (today CM
  L1134 to L1141 give each part its own, and take the container's away).
- `zoomBoxOf` (CM L25): the container for a part too, as the box that zooms and is selected; Z6 takes its point from
  the part under the pointer.
- `applyZoomLevel`: the range by the regime; below 100% on a picture that fits, a width in pixels and the
  `diagram-zoomed-out` mark, with no scroll or grab; the window scroll of Z6; every part of a diagram in parts, near
  ones first (Z8); in R0, for a part, `overflowX` set before the scroll (B3).
- The controls follow the window down their box (B4, Z10): one passive scroll listener, for the box under the pointer
  and the selected one only, moves the controls within the box with a `transform`, in an animation frame.
- The wheel and key handlers keep their code: they now find a slider on every drawn diagram.
- `ZOOM_OUT_FLOOR`, the constant of Q1: 50.
- The picture items (Z13): `wholeSvg(container)`, new, builds one SVG from every part's `lookClone` (CM L100), its ids
  prefixed per part and the parts' instructions left out, and hands it to `serializeSvg` (CM L161) and the canvas
  code; `svgToCanvas` and `svgToCanvasWithBg` (CM L208, L225) take the largest scale up to 2 that fits a canvas;
  `encodeSource(text)`, new, gives PlantUML's encoding of the container's source for the one instruction.

RS: `renderFragments` (RS L1121) and the parts of a "too large" retry (RS L1418 to L1430) put `hide footbox` after
the `@startuml` of every part but the last, and with R2 the spacer row, into the queue item's `source` only (Z12).
CN: `zoomsBefore` and `carryPartZoom` give way to the container's state (4.2), and a first drawing's part that lowers
the diagram's level re-applies it (Z8); the decorate restore (CN L3469) covers `diagram-zoomed-out`; a redraw in parts
adds the same lines as RS does, in the same place. IS: the rule that shows a part's own slider
(`.puml-fragment:hover > .diagram-zoom-controls`, IS L59) goes, and the placeholder (IS L20 to L38) shows on the first
part not yet drawn only. No C# changes: the report's markup stays as it is, and only page scripts change.

### 4.4 Performance

On the consumer report a slider is made for the boxes that fit (217 at 1920 and 57 at 1366, counting parts, which Z8
then gathers into one slider per diagram), in the same way as today: lazily as a box comes within 200 px of the screen,
and after the paint for a redraw (5.5.1). The natural width is read from the picture's `width` (5.5.1); the padding adds
one computed-style read per slider. The gate: `ReportToggleResponsivenessTests`' budgets unchanged, and the 5.5.1 toggle
bench on the consumer report before and after, reported with R1.

A diagram in parts zooms all its parts (Z8). Laid out at once, a notch costs 17 ms on a 3-part diagram, 62 ms on an
8-part one and 191 ms on the report's 15-part one, against 4 to 14 ms for one part today (M6). Resized near the screen
first, a notch costs what the parts on screen cost; the budget for R1: a notch on the 15-part diagram paints within
50 ms, and its other parts are all resized within a second. If that cannot be met, the next step to measure is a zoom
by `transform`, which scales a picture without laying its text out again, at the price of a wrapper sized by hand.

### 4.5 What changes for a reader

- Every diagram shows a slider on hover. A diagram that fits has its slider at the right end, and moving it left
  shrinks the diagram.
- Ctrl with the wheel, or a trackpad pinch in Chromium and Firefox, over a diagram zooms that diagram, never the page.
  Over the rest of the report it zooms the page as before, and the browser's own zoom (its menu, Ctrl and 0) is
  untouched. Q6 puts this to the owner as a decision.
- Zooming a long diagram no longer slides what is under the pointer down or up the page.
- A long diagram drawn in parts reads at one text size from top to bottom, has one slider that stays in reach while
  the reader is inside it, and zooms and pans as one. Parts drawn at full size today can be drawn smaller, at the
  level that lets the widest part fit. With R2, its participants also stand in the same places in every part, and the
  diagram is as wide as it would be drawn whole: on the report no wider than its widest part for 24 of 39 diagrams, up
  to 12.2% wider for 11 (M11).
- A click anywhere on a diagram in parts rings the whole diagram, and copying or saving its picture gives the whole
  diagram (one too long for a PNG gives the part clicked, and its menu says so). It ends once, with the participants
  under its last message only, and each part keeps the participants' names at its top.
- The source a reader copies or opens is the diagram as written, whatever the page added to draw it.

### 4.6 Alternatives

- **Swallow Ctrl with the wheel over a diagram that fits, and do nothing.** It stops the page zoom, but leaves a
  gesture that does nothing; the owner asked for zooming out.
- **Hand the wheel to the page at the ends of a range.** The surprise comes back at the edges, and a wide diagram at
  100% already keeps the wheel.
- **Zoom a fitting diagram in past 100%.** No diagram is drawn larger than its natural size today, and the ask is
  zooming out.
- **Keep a zoom per part (5.2.2), each part with the two ranges.** The plan's first version. The text of one diagram
  then changes size from part to part (scenario 1: 54.8%, 100% and 72% at 1366), which the owner found jarring (Q2).
- **Hide a long diagram until every part is drawn, so its scale never drops.** It would delay the first picture of
  every long diagram by its whole drawing to avoid one drop in 2 to 6 of its 24 diagrams.

## 5. Bugs found on the way (R0)

- **B1, the fit counts the box's padding.** `isDiagramZoomable` and `getFitPercent` (CM L1032, L1039) use the box's
  `clientWidth`, while a whole diagram's container has 1em of padding on each side (IS L10, L141) and its picture is
  drawn no wider than the content box (IS L4). A picture between the two widths is drawn scaled down with no slider (10
  boxes at 1920, 2 at 1366, M5), and a wider picture's slider starts above the level it is drawn at, so its first step
  jumps (scenario 10: 66.8% drawn, 68% shown, 70% after one step). Fix: Z3. Parts have no padding of their own and
  are not affected.
- **B2, a zoom moves the point under the pointer down the page.** `applyZoomLevel` keeps the point across (CM L1069 to
  L1088) and does nothing down the page, though the picture's height changes with its width: one notch moved it 91.2
  px on a 5,900 px diagram (M4). `GradualZoomTests.Zoom_in_keeps_artifact_under_cursor_in_place` (L470) checks across
  only. Fix: Z6.
- **B3, a part's first zoom step loses its place across.** `applyZoomLevel` sets the box's `scrollLeft` (CM L1088)
  before it makes the box scroll (`overflowX = 'auto'`, CM L1107). A whole diagram's container scrolls from the
  stylesheet (IS L9), a part does not, so the first step on a part scrolls nothing: 6.6 px off at 1920 and 24.4 px at
  1366 (M4). Fix: set the scroll after `overflowX`. R1 retires a part's own scroll (Z8); the fix is for the releases
  between.
- **B4, the slider of a tall box never follows the page.** The controls are `position: sticky; top: 2em` (IS L44 to
  L46), meant to stay in the window while their box is. The container's `overflow-x: auto` (IS L9) makes its
  `overflow-y` compute to `auto`, so the container, which never scrolls down, is the sticky element's scroll box: 1,500
  px into a part, its slider is 1,472 px above the window (M6), and a reader in the middle of a tall diagram has to go
  back to its top to reach the slider. Fix: Z10.

## 6. Tests

All test-first (CLAUDE.md): each new fact fails on the code before its release, and its failure line goes in the
release's commit message. Since 5.6.0 a report page is a loader page: the E2E facts open a report through
`GotoReportAsync` and the helpers beside it (`tests/Kronikol.Tests.EndToEnd/ReportPageExtensions.cs`) and read a
page's text with `ReportFileText.Read` (5.6.0, `plans/HTML_COMPRESSION_PLAN.md` R1).

### 6.1 R0, red on 5.5.3

- `GradualZoomTests.A_picture_wider_than_its_box_s_content_has_a_slider_starting_at_the_level_it_is_drawn_at` (B1). The
  test sizes the window so that the box's content is 10 px narrower than the picture's natural width; red: no slider.
- `GradualZoomTests.The_slider_s_lowest_level_is_the_level_the_picture_is_drawn_at` (B1): within half a point; red: 68
  against 66.8 on a fixture like scenario 10.
- `GradualZoomTests.A_ctrl_wheel_step_keeps_the_point_under_the_pointer_in_place_down_the_page` (B2): a tall wide
  diagram, a notch in and a notch out at a point halfway down the window, the point within 2 px; red: 91 px.
- `SplitDiagramZoomTests.The_first_ctrl_wheel_step_on_a_part_keeps_the_point_under_the_pointer_across` (B3): within
  2 px; red: 6.6 px.
- `GradualZoomTests.The_slider_of_a_tall_diagram_stays_in_the_window_while_the_diagram_does` (B4): the page scrolled
  1,500 px into a tall diagram, hovered and then selected, the slider's top within the window; red: 1,472 px above it.

### 6.2 R1, red on R0

First, with no change of behaviour (it passes on R0): each of the 48 reads of the page's first slider (2.4) reads the
slider of the box the test acts on, through one helper in `PlaywrightTestBase`. To show they were the risk: with a
throwaway change giving every fitting box a slider at 100, the old reads fail where the new ones pass; the throwaway is
not kept.

A new class, `FittingDiagramZoomTests` (the `Zoom` collection), on a report with a wide diagram followed by one that
fits, and on `GenerateReportWithWideDiagramInParts` with a fitting last part:

1. A diagram that fits has a slider at 100 whose lowest level is the floor.
2. Ctrl with the wheel down over it shrinks it: narrower, in the middle of its box, the slider below 100, and the event
   prevented.
3. Ctrl with the wheel up over it at 100 is prevented, and it stays at 100.
4. Selected, Ctrl and - shrink it and Ctrl and + bring it back, never above 100.
5. Zoomed out it has no grab cursor, no scroll, does not pan, and a drag over it selects its text.
6. The owner's case: a wide diagram then one that fits; Ctrl with the wheel over the second zooms the second only, and
   the event is prevented.
7. A diagram in parts with a wide part and a fitting part has one slider, on its container and none on a part, starting
   at the smallest fit level of its parts, and every part is drawn at that level (Z8; facts 14 to 18 follow it).
8. A zoomed-out level is kept when the headers toggle, a note's button and a report control draw the diagram again.
9. A zoomed-out diagram drawn again wider starts at its fit level, not zoomed in.
10. A fitting diagram in a box not laid out gets its slider at 100 when shown (with 5.5.3's fact).
11. A fitting component diagram (the component diagram page) and a fitting activity diagram in the internal-flow popup
    get a slider.
12. Copy as PNG of a zoomed-out diagram gives an image of the natural size.
13. At 768 px and below no controls show (the existing pin, kept).
14. Ctrl with the wheel over the fitting part of a wide diagram in parts zooms every part to one level and keeps that
    part's point under the pointer, across and down the page; the event is prevented.
15. A drag over a zoomed diagram in parts pans every part together, and leaves the diagram selected or not as it was.
16. A first drawing whose second part is wider than its first lowers the first part's level once, and the window keeps
    the reader's place (a fixture with a narrow first part and a wide second part).
17. A notch on a generated 15-part diagram (as wide and long as M6's scenario 278) paints within 50 ms, under the
    suite's contention-scaled budget as `ReportToggleResponsivenessTests` uses, and every part reaches the new level
    within a second.
18. A diagram in parts drawn whole again (Details from expanded to truncated), and drawn in parts again, keeps its
    level, direction and selection on the container.
19. A click on any part of a diagram in parts selects the diagram: its container has `diagram-selected`, its ring
    encloses every part's box, no part has the class, and a second click takes it off.
20. Copy as SVG over the second part of a 3-part diagram gives one SVG that parses as XML, holds the three parts'
    pictures in order (each part's texts within its own place), uses no id twice, and has one `plantuml-src`
    instruction, which decodes to the container's source.
21. Save as PNG over a diagram in parts gives the whole diagram at twice its natural size; over a generated diagram too
    long for a canvas at its own size, the PNG items read "part 2 of N" and give that part.
22. Every part of a diagram in parts but the last is drawn with no footbox (no participant box under its last
    message), and the last with one; no part's `data-plantuml` and no copied or opened source holds `hide footbox`.
23. On a first drawing, only the first part not yet drawn shows "Rendering diagram…".
24. Copy Highlighted Text over a selection from the first part into the second gives the first part's rows, then the
    second's. A pin of today's behaviour (Z13, "already one"): it passes before R1 and holds that R1 keeps it.

Pins that move: `DiagramZoomTests.Non_zoomable_diagram_has_no_zoom_slider` and
`GradualZoomTests.Zoom_slider_does_not_appear_on_non_zoomable_diagram` become facts that a fitting diagram's slider
starts at 100; `ZoomKeptOnRedrawTests.A_diagram_drawn_again_to_fit_its_box_takes_no_zoom` keeps its assertions but its
slider one, which turns to a slider at 100; the two `DiagramContextMenuTests` pins hold the new gate's text. Each keeps
its history comment and adds the release that changed it.

`SplitDiagramZoomTests` (5.2.2, 10 facts) holds a zoom per part, which Z8 replaces. Each fact keeps its situation and
turns to the diagram's one zoom: one slider on the container (L64); the wheel over the second part zooms every part,
keeping the second part's point (L84); a click on the second part selects the diagram and the keys zoom every part
(L104); hovering the second part shows the diagram's slider in the window (L122); a drag pans every part (L148); and the
five redraw facts (L191 to L295) keep the level, place and selection on the container. The 5.2.5 fact on a selected
part that a redraw takes away (`ZoomKeptOnRedrawTests`, L269) turns to the diagram staying selected. B3's R0
fact goes with the part's own scroll.

### 6.3 R2, red on R1

A new class, `LinedUpPartsTests`, on a fixture of three parts whose widest gaps fall in different parts:

1. Every part's lifelines stand at the same places, within 0.5 px at one level, the first lifeline included (the page
   places each part).
2. The row draws nothing: each part has as many arrowheads, lines and texts as drawn without it, and no text of
   underscores; each part is the row's height taller (about 39 px, M7).
3. Each message keeps the number it has without the row.
4. The owner's ask (Z12): "Open full PlantUML source in new tab", "Open current PlantUML source in new tab" and both
   Copy source items, on a lined-up diagram in parts with a note expanded, give text with no `-[hidden]>`, no
   `autonumber stop`, no `<color:transparent>` and no `hide footbox`, equal to the container's full and current
   source.
5. Copy as SVG of a lined-up diagram: its one `plantuml-src` decodes to the container's source, with no row.
6. No part's `data-plantuml` holds the row, and the current note text of a note in the second part is as without R2.
7. A toggle that draws the diagram again in parts lines its new parts up; one that leaves a single picture adds no row.
8. A diagram whose parts disagree once drawn (a test hook halves the outline's gaps) is drawn again with the row sized
   from its parts, and lines up.
9. A lined-up diagram starts at its fit level, all of it in view (Q8, "start at fit").
10. The stress case: the 5.5.1 toggle bench on a generated 15-part diagram within 5% of R1's (M10: +1% on the report's
    long diagrams).
11. The row is exact: a lined-up diagram is no wider than the same diagram drawn whole, by at most 1 px a gap, on a
    fixture whose pairs of participants start their labels from different gaps (M11; red on M10's row, 9 to 18 px a
    gap wider).

What R2's first step settles (Q7) gets a fact of its own: a gap wider than one label pushes, and participants declared
with no `as`, in a `box` or by `create`.

### 6.4 Proofs

Beyond red-first, a mutation script in the harness (node, as this machine has no Python) applies each mutation to the
built page script, runs the named facts, and requires a failure: no slider for a fitting box; the floor ignored; a
fitting box starting at the floor; the wheel not prevented at 100; a zoomed-out level given to a wide picture, and a
zoomed-in one to a fitting picture; a redraw dropping `out`; the decorate restore ignoring `diagram-zoomed-out`; a
zoomed-out box given the grab cursor and pan; the window scroll of Z6 removed; a part's scroll set before `overflowX`
(R0); the content width back to `clientWidth`; the controls left where `sticky` puts them (B4); a diagram's level
taken from its first part, not its smallest fit; only the part under the pointer resized; the far parts never resized;
a part above the window resized without the window scroll; the ring put on the part clicked; a picture copy of the
part under the click; a part's ids left as they are; a part's instruction left in the copy; the footbox kept on a
middle part or taken off the last; `hide footbox` or the row written into a part's `data-plantuml`; the row in a
copied or opened source; the row sized at zero; `autonumber stop` left out; the check after drawing skipped.

### 6.5 Browsers, and what a person checks

The Zoom collection runs as the E2E project runs it. Pinching on a trackpad is Ctrl with the wheel in Chromium and
Firefox, which these facts cover; Safari's pinch is a gesture event (Q5). Left to a person: how zooming out a fitting
part feels with a trackpad and with a mouse wheel, at 1920 and on a laptop, on the consumer report.

## 7. Releases

### 7.1 Order and version

1. 5.5.3 and 5.5.4 came out while this was written; R0 starts from `origin/main`.
2. **R0**, a patch: B1 to B4, bug fixes with nothing new to call or configure.
3. **R1**, a minor: a slider on every diagram and zooming out a diagram that fits (floor 50%, Q1) are a new report
   control, and a diagram in parts acting as one (Z12, Z13) goes with them (CLAUDE.md, MINOR).
4. **R2**, a minor: the parts of a diagram lined up is a new diagram feature (CLAUDE.md, MINOR). Numbers are taken
   when each is pushed.

### 7.2 Where

A worktree per release from `origin/main` (`git -c core.autocrlf=false -c core.eol=lf worktree add`). This plan was
written in `C:/Code/Kronikol-zoomall` (branch `plan/zoom-every-diagram`).

### 7.3 Documentation

- The changelog, saying which part moved and why.
- The wiki, `PlantUML-Browser-Rendering.md`: for R1 a new "Zoom" section above "Zoom on a diagram drawn in parts
  (5.2.2)" (wiki L158) saying which diagrams zoom, the two ranges, the wheel and keys over a diagram and the page
  outside it, and pan; that section (L158 to L169) rewritten for one zoom per diagram, with a line saying 5.2.2 to
  R1 zoomed each part on its own, and that a diagram in parts is selected and copied as one and ends once; and L175
  for a diagram drawn again to fit. For R2 the same section says that the parts stand in line, how the page does it,
  and that no source a reader copies or opens holds the row. R0's fixes in the changelog only.
- Kronikol4J: a line in the divergence ledger for each release, as the page scripts change.

### 7.4 Acceptance

On the consumer report, at 1920 x 1000 and 1366 x 768, with the harness's probes: every laid-out diagram has a slider
once brought near the screen; scenario 1's three parts are drawn at one level, and Ctrl with the wheel over each is
prevented and zooms all three; a diagram that fits shrinks to the floor and no further; one notch moves the point under
the pointer no more than 2 px either way on a wide diagram, and down the page on a fitting one; a notch on scenario 278
(15 parts) paints within 50 ms; the slider is in the window anywhere inside a tall diagram; the toggle bench within 10%
of R0's. The stress case is the feature holding scenarios 277 and 278 (M9): on 5.5.2 its toggles put the new pictures on
screen in 0.7 to 1.1 s and finish in 1.9 to 2.5 s, with no task over 0.55 s, and no release of this plan may make them
slower. Also for R1: a click on any part of scenario 1 rings the whole diagram, its SVG and PNG copies are the whole
diagram, scenario 278's PNG items name the part, and no seam shows a footbox. For R2: every diagram in parts on the
report lines up to 0.5 px on its one drawing, adding no more than M10's median 20 ms, is within 1 px a gap of its
drawing whole (M11), and no source the menu gives holds the row.

## 8. Questions for the owner

- **Q1, the floor for a diagram that fits: answered, 50%** (the owner, 2026-10-23: "yes to all three"). Recommended
  as 50% because readers already see wide diagrams there on a laptop (a
  median of 58% and a p10 of 49% at 1366, M1); text paints at 5.5 to 6.5 px; and 82 of the 171 tall fitting diagrams
  at 1920 can then be seen whole (21 of 31 at 1366, M3). 25% shows more of them whole (115 and 27) but paints text at
  about 3 px, an outline only. A floor that fits the whole picture in the window would give a short diagram no range
  at all, the inconsistency the ask removes. Recommended as a constant, not an option.
- **Q2, one scale for the parts of a diagram: answered, in R1 (Z8).** Asked because 15 of the 24 diagrams in parts at
  1920 (13 at 1366) mix wide and fitting parts, so the text of one diagram changes size from part to part (scenario 1:
  54.8%, 100% and 72% at 1366). The owner, 2026-10-23: "Tbh, that sounds better, unless there's a reason not to? The
  scaling differences can be jarring." Measured against it (M6), no reason not to was found; what it costs: parts that
  fit today are drawn smaller (42 of 68 at 1366, by 15.4 points at the median and up to 52), which the reader can
  zoom back, all parts at once; a first drawing shrinks the parts already shown once when a later part is wider (2
  diagrams at 1920, 6 at 1366); a notch has every part to resize (191 ms on 15 parts at once, so near parts go first,
  4.4); and 5.2.2's zoom per part, its 10 facts and its redraw code are replaced. It does not line the parts up (Q7).
- **Q7, parts laid out alike: answered, R2 is in, after R1** (the owner, 2026-10-23: "yes to all three"); measured
  in M7 and M10. One scale makes a
  diagram's text one size, but the engine lays each part out on its own, so a participant stands at a different place
  in each part (scenario 1's first lifeline at 355, 577 and 434 in its three parts' own units, M6). The owner asked
  whether that can be fixed. It can: PlantUML has no setting that places a participant, but a gap between neighbours
  is the widest any message between them needs, so one row of hidden messages after the participants, its labels runs
  of transparent underscores sized to each gap's widest in any part, holds every part to the same gaps. On scenario 1
  all three parts then have gaps of 744.2, 78.6, 377.4 and 771.1 px, to 0.1 px; the row draws nothing (as many
  arrowheads and lines as without it) and makes each part 39 px taller. The space left of the first participant still
  differs (355.1 against 25.4), which the page evens out by placing each part. How R2 would work:
  1. Before a diagram in parts is drawn, the page draws a compact outline of it (M10): the participants and, for each
     kind of row (a message's two participants and its arrow, with the side of an attached note), only the widest label
     and the widest note, each note cut to its first line, its two widest lines and its table rows, and the widest step
     bar (M10's lean outline; keeping the three widest of each, notes whole, cost three times as long for no gain in
     lining up). It reads the outline's lifelines (as the role
     pass does, RS L1944 to L1949) and draws every part once with the row sized from its gaps. Nothing moves on screen.
  2. The labels: an underscore pushes its gap 1.08 px, linearly to 1,974 px at 1,800 underscores (M7), but the gap a
     label starts from differs from one pair of participants to the next, and on one row a label's room depends on its
     neighbours' (M11): sized from one calibration, the row made most gaps 9 to 18 px wider than asked. So the page
     draws the row over the participants alone first, a few milliseconds, and corrects each label by its gap's error,
     and a gap the participants alone make wide enough gets no label. Corrected three times, the row still missed by up
     to 17 px on the report: R2's first step makes it exact, by labels that do not share their room (each spanning from
     the first participant, or each on its own line if that is the only way) or by more correcting drawings, whichever
     costs less. The row stops the numbering around it (`autonumber stop`, then the part's own `autonumber`):
     numbered, its messages made the gaps of a part numbered from 38 wider by 7.5 px. The outline gives each message it
     keeps its own number (`autonumber N` before it).
  3. Once its parts are drawn, the page checks that their gaps agree; a diagram whose parts disagree is drawn again with
     the row sized from its parts (M7's way, the second drawing M8 measured). With M10's row none of the 40 needed it,
     as that row was wider than asked and hid where the lean outline falls short; with the corrected row the parts
     disagreed by over 1 px in 24 of the 40 (M11). The first step also finds the outline that is never short (M10's
     three widest of each kind, notes whole, fell short once), or keeps the lean one and counts the second drawings.
  4. A redraw (a toggle) draws the outline of its new source first, in the same way.

  What it costs against today (M10, the report's 40 diagrams in parts, each drawn both ways in turn): the outline
  (median 18 ms) holds back the parts, so a diagram in parts is drawn 20 ms later at the median (-19 to +118 ms): no
  difference that can be measured on the long ones (15 parts +1%, 8 parts +1%), +16 to +36% on a typical 2-part diagram
  (64 to 80 ms at the least), +26% over all 40; and each part 39 px taller. And the lined-up diagram is as wide as the
  same diagram drawn whole, which no row can beat, as its parts then share one set of gaps (M11): no wider than its
  widest part today for 24 of the 39 diagrams that can be drawn whole, 0.9 to 3.9% wider for 5, and 8.6 to 12.2% wider
  for 11, so their text is drawn that much smaller. Those 11 have a wide reply note in a part other than the widest,
  where the widest part leaves its column narrow. M10 measured 2.1% at the median and up to 16%: the rest was the row,
  wider than asked (M11).
  Measured before it and set aside (M8): drawing every part a second time (as long again as the first drawing, 1.1 s on
  15 parts, with the parts moving into line on screen) and an outline of the whole diagram (31 s on 15 parts). What R2's
  first step settles: a gap wider than about 1,970 px (2,000 underscores drew an error picture, so a wider character),
  and sources whose participants are declared otherwise (no `as`, a `box`, one created partway with `create`).
- **Q3, wide diagrams below their fit level.** With Q1's floor a wide picture could also run from the lower of its fit
  level and the floor, which would show more of a tall wide diagram whole. Not asked; not in R1.
- **Q4, a level readout** ("80%") beside the slider. Not asked; today the slider's title is "Zoom level".
- **Q5, Safari.** A trackpad pinch there is a gesture event, not Ctrl with the wheel, and zooms the page over every
  diagram, wide ones included, today and after R1. Handling it would bring Safari in line. Not asked.
- **Q6, the page zoom over diagrams.** After R1, Ctrl with the wheel or a pinch over any diagram zooms the diagram, and
  an open scenario is mostly diagram, so a reader who zooms the page that way has to move the pointer off the diagrams
  (or use the browser's menu, or Ctrl and + with no diagram selected). That is the consistency asked for; it is listed
  so it is a decision rather than a surprise.
- **Q8, with R2, start a lined-up diagram at today's level? Answered: no, at its fit level** (the owner, 2026-10-23:
  "'start at fit' is better"). Lining up makes a diagram as wide as it is drawn whole, up to 12.2% wider than its widest
  part (M11), so at its fit level its text is drawn up to 11% smaller, and the reader can zoom it back, every part at
  once, up to the same 100% as today. The page could instead start it at the level its widest part would have had, which
  costs no time (a zoom is the pictures' width, set once when they are drawn either way): its text is then today's size,
  and the extra width, which lies between the participants, runs off the right edge, so the rightmost participant and
  its notes start out of view. That breaks Z2's rule that a wide diagram starts at its fit level, all of it in view.
  Recommended: keep the fit level, as for every other diagram. Asked by the owner as an idea; not in R2 unless chosen.
- **Q9, with R2, line up only where it is cheap?** Skip the row for a diagram that lining up would widen by more than a
  set share (say 5%): on the report, of the 39 diagrams in parts that can be drawn whole, 28 widen by 1.1% or less, 1
  by 3.9% and 11 by 8.6 to 12.2% (M11). Those 11 would keep today's text size and their sideways jumps, so diagrams
  would differ in whether they line up. Not
  recommended over Q8's fit level; listed with it as the other way to spend less width. Not taken: the owner chose
  the fit level (Q8) and did not take this, so every diagram in parts lines up.
- **Q10, a PNG of a diagram too long for one canvas: answered, as recommended** (Z13; the owner took every
  recommendation on 2026-10-24). A browser draws a canvas of about 32,767 px a side at most, and the PNG items draw at
  twice the natural size: on the report 4 of the 40 diagrams in parts are too tall for that at twice and 3 at their own
  size (the longest 115,572 px). Recommended: the largest scale up to 2 that fits, and for a diagram too long at its own
  size the part under the click, its PNG items saying which ("Copy part 3 of 15 as PNG"); its SVG items still give the
  whole diagram, which has no such limit. Set aside: drawing below its own size (scenario 278 at 0.29, its text under 4
  px, an outline only), and one PNG per part (a copy holds one image, and a browser asks before several downloads). R1's
  first step measures the limit in each browser the E2E run covers.
- **Q11, the footbox at the seams: answered, as recommended** (Z13; the owner, 2026-10-24). Recommended: every part but
  the last is drawn with `hide footbox`, and every part keeps its heads. Hiding the heads too would make the parts read
  on as one picture, but this plan found no directive that hides them (drawn transparent, they would still take their
  place), and deep in a long diagram the heads are the only names of the lifelines on screen; with R2 they also stand
  exactly over the lifelines of the part above. Keeping a footbox on every part is today's look, and keeps a seam
  looking like the end of a diagram.
- **Q12, narrower diagrams, so that lining up costs less** (the owner, 2026-10-24: "Are we absolutely certain there's
  no way to improve on this 2-16% situation, maybe make it 2-7% (and I'm not talking about 'only doing it for some
  diagrams and not others')"). Not without drawing the diagrams themselves differently. Lined up, the parts share one
  set of gaps, so stacked they are one drawing of the whole diagram, and the engine draws none narrower (M11). M10's
  2.1% and 16% were partly the row, wider than asked; made exact (Q7, step 2), R2 costs the floor: nothing at the
  median, at most 12.2%, 11 of the 39 over 7%. Those 11 are wide because each payload note takes a column of its own,
  a request's beside the caller and a reply's beside the service replying, and a part other than the widest holds a
  wide reply note where the widest part leaves the column narrow. Measured, each applied to every diagram alike (M11):
  - **A reply's note on the left of its arrow**, beside the caller for the test's own calls, so a call's two payloads
    share one column: every lined-up diagram within 3.0% of today's widest part (4.1% narrower at the median), and 475
    of the 546 pictures narrower. But 58 get wider, and three ordinary diagrams are drawn 12 to 15% smaller (scenario
    288 at 68.6% for 80.9%): it moves the worst case onto other diagrams rather than removing it, and changes where
    every reader finds a reply. Asked by the owner whether the loss falls on diagrams that start at 100% and so have
    room to spare: 4 of the 20 losses do; 16 fall on diagrams already zoomed out, while 315 others gain about 1% (M11).
    Lining up itself spends room where there is room: a diagram that fits its box stays at 100% when it widens.
  - **A note under its message**, across its two participants: every lined-up diagram at least 13.8% narrower than
    today (24.0% at the median), and 23.7% taller at the median, up to 29%.
  - Set aside: a request's note on the right of its arrow (up to 19.9% wider), notes wrapped at 600 px for 800 (no
    narrower at the worst, up to 13.2% taller), and a part leaving out the participants it has no message for (3 of the
    40 narrower, none of the 11).

  Choosing for each diagram the side that draws it narrower would keep every lined-up diagram within 3% and draw none
  wider than today, but a reader would find replies on one side in one diagram and on the other in the next, which this
  plan's consistency rules out as it rules out Q9. Recommended: none of these to buy back the 11; R2 with its row exact,
  so a diagram in parts is as wide as it would be drawn whole, like every other diagram in the report. Where reply notes
  go is a choice about every diagram's look, worth making on its own merits if at all.

  **Answered: R2 with its row exact, no note moved** (the owner, 2026-10-24: "ok, so I guess we should go with the
  median 1% worst 12.2% thing then - that's what you recommend?"). The 1% was the row as corrected in M11; made exact,
  as R2's first step does, it costs the floor, 0 at the median.
- **Q13, PlantUML changes that would help** (the owner, 2026-10-24: "are there any plantuml issues/prs we could raise
  that would help with this?"; M12). None is filed; each would go up from the owner's account. None changes the floor:
  a lined-up diagram is as wide as the diagram drawn whole, which is PlantUML's layout of its content. In the order
  recommended:
  1. **A sequence diagram's layout grows faster than the diagram once its messages carry notes**, a bug report with a
     reproducer, on its own or in #2834, where the maintainer took the last fixes from. 800 request and reply pairs
     with a 6-line note on each reply take 21.9 s in the browser build against 1.1 s for 400, and 2.7 s against 0.6
     s in Java; without notes, 800 pairs take 0.19 s. 67.5% of a Java profile is `AbstractReal.getCurrentValue`
     under `RealDelta.getCurrentValueInternal`, which recomputes a position through its whole chain on every read;
     `d087fc0c4` (2026-07-15) put teoz's vertical positions on that line. A fix there (a value cached until the line
     moves, or a delta of a delta folded into one) is for the PR to choose and prove with identical SVG, as the
     #2834 patches were. It is why Kronikol cuts long diagrams at all: scenario 278 takes 39 s whole against 1.3 s
     in parts (M9).
  2. **A spacing between two neighbouring participants that draws nothing**, a feature request. R2 holds a gap open
     with a row of hidden messages, whose labels the engine keeps apart from each other on purpose
     (`PlayingSpace.addParallelSiblingDisjointConstraints`), which is why the row needs correcting drawings (M11),
     and which makes every part 39 px taller. A primitive that only sets a least distance would make the row exact
     at once and take no height.
  3. **The browser build drawing any page of a `newpage` diagram, all pages from one layout**, a feature request.
     Teoz already lays a diagram cut with `newpage` out once and draws every page from it, each as wide as the
     whole and with the participants' heads at its top (since #2104), so its pages line up by themselves; but
     `render` and `renderToString` draw page 0 only, and each drawing lays out the whole diagram (page 0 of scenario
     492's 8 costs 1,150 ms, the whole 1,334 ms). With 1 fixed and all pages drawn from one layout, Kronikol could
     cut a long diagram with `newpage` and need neither R2's row nor its outline. Worth asking only after 1.
  4. Small: `PlantUMLBrowser.buildSvg` compares a picture with `maxSvgSize` only after drawing it, so a diagram too
     large is drawn in full before it is refused (scenario 278, M9); and an option for the source the picture
     carries as `plantuml-src`, which Kronikol otherwise replaces itself (Z12).

## 9. Log

- **2026-10-24, executed as 5.7.0.** The owner: "Can you implement the plan in full as a minor release". R0, R1 and R2
  ship together as 5.7.0 instead of the three releases of 7.1, in a worktree of their own while another session
  released 5.6.1 from `main` (rebased onto it before the full runs). What the work changed from the plan as written:
  - **Z1, the internal-flow popup.** The popup draws its activity diagram itself, outside the render queue
    (`internal-flow-popup-script.js`), so RS L1385's call to `addZoomButton` never reached it. It now asks for the
    slider after it draws a picture (fact 11 of 6.2).
  - **Z2, "wide"** is "its fit level, rounded, below 100": a picture a fraction of a pixel wider than its box got a
    slider from 100 to 100.
  - **Z6** also holds off the browser's own scroll anchoring (`overflow-anchor: none` on the root, from the change to
    the frame after): it moved the page by the height a zoom changed above the screen, on top of the page's own scroll.
  - **The cursor (Z5, Z11).** The grab cursor, and a drag's grabbing cursor and `user-select: none`, were written on the
    zoomed box. With one box for every part, each change restyled every element of every part, as both properties are
    inherited: 55 ms a change on fact 17's fixture (36,780 elements), paid by every first zoom step and every drag. They
    are CSS rules on the picture under the pointer now (`inline-svg-styles.css`, the `diagram-panning` mark).
  - **R2's row.** `<space:N>` labels replace the transparent underscores of Q7. A run of underscores is creole
    underline (`__x__`), so 5q + r underscores draw q + r characters, and a label's width is not monotonic in their
    number: that was M11's 17 px, which no correction could settle. `<space:N>` is N px exactly and draws nothing; a
    hidden message holds its gap at N + c, with c measured once a page for each header (24 px on a plain teoz header,
    32 px on the consumer report's). No correcting drawing is needed; the check of step 3 stays, and it never fired on
    the consumer report (fact 8 forces it with a hook that halves the outline's gaps).
  - **R2's outline** (step 1): for each kind of message, the one with the widest label and the one with the widest note,
    notes cut, no step bar or note across, each message with its own number, and labels measured on the text a link
    shows (a link's target is never drawn, and its varying length misranked labels by up to 13.5 px). Never short on the
    40 diagrams in parts of the consumer report, where M10's lean outline fell short on 4 and three of each kind on 1.
    Each part's header is skipped by the splitter's own rule (`parseDiagramStructure`).
  - **Lifelines** are the vertical lines covering at least half as much as the one covering most: the role pass's rule,
    a third of the picture's height, missed them on a short last part, whose heads and footbox take most of it.
  - **A bug in the splitter, found by Q7's fact for a `box`:** `parseDiagramStructure` ended a diagram's header at a
    `box` line, so every part after the first declared no participant and drew only those its own messages named, in
    the order it met them, without the box. The header now takes `box` and `end box` lines.
  - **Fact 17's budget.** On the consumer report's scenario 278 (15 parts of about 4,400 elements) a notch paints in 50
    to 55 ms. The browser alone takes 33 to 42 ms to draw that one part at another size (its layout, pre-paint and
    paint), and as long when the part is scaled by a `transform`, which lays SVG text out again just the same: 4.4's next
    step was measured and gains nothing. M6's 4 to 14 ms a part was the layout alone. The fact holds a notch to 50 ms,
    or to the browser's own cost of growing that part one level past its box, measured in the fact, with a quarter more
    and 10 ms, whichever is more: 73 ms against 60 ms for the part alone on the fixture, every part at the new level
    within 0.7 s.
  - **Fact 10 of 6.3** times the toggles of one page, lining up switched off and on between them
    (`window.__kronikolLineUp`) once every picture is in the page's cache, each toggle started once the last has
    settled, and compares the quicker half of each kind (7 rounds of Collapsed and Truncated each way). Two pages timed
    against each other passed one run and failed the next: the same toggle took 800 to 1,190 ms. Three runs of the fact
    alone gave 1.00, 1.02 and 1.03 of the toggle without lining up, against 5% and two frames.
  - **Fixtures.** The report sorts scenarios by name, so the parts fixture's second scenario is "Short diagram" (as
    "After" it came first). Truncated is the report's default and cuts a note at 10 lines, so the facts that need
    Truncated and Expanded to differ use notes longer than that (`PartsSource`'s `extraNoteFields`). A diagram drawn in
    parts and drawn again in one piece is one part, not the box's own picture.
  - **The mutation run** is `ZOOM_EVERY_DIAGRAM_PLAN.harness/mutate.js`: 6.4's list, with six of this release's own,
    32 in all. Its first run killed 21 of 30; the nine that survived each fail a fact now. Fact 9 zooms out to 95, above
    the fit level the picture gets, as a kept level below it changed nothing. "A redraw dropping out" as first written
    (`_zoomState.of` reading no zoomed-out mark) changed nothing, as the box keeps its marks through every redraw; it
    removes `levelFor`'s zoomed-out rule now. "autonumber stop left out" keeps every number (each part's own
    `autonumber` comes after the row) and widens the row's gaps by their numbers, which fact 11 catches. The copy fact
    holds the copied SVG to one `plantuml-src` in all, comments included (the engine writes one into every part). Q7's
    fact has a note left of the first participant in a later part only, so a part's first lifeline differs. Three facts
    are new: a zoomed-out diagram drawn again is at its level in the first frame showing its new picture; a diagram in
    parts that is not lined up takes its level from its widest part, a later one; and a notch far down a diagram in parts
    keeps the point under the pointer while the parts far above it grow. That last one failed on the code as written,
    twice over: the window scrolls by whole pixels and each part resized a frame after the notch lost the remainder (5 px
    down over 16 parts; `keepPlace` now hands it to the next part), and one level past the fit level the parts were wider
    than the box's content but not its padding, which a box counts only past its own children, so nothing scrolled and
    the point moved 6.1 px across (a zoomed-in part is now as wide as its picture, `inline-svg-styles.css`). It also
    kills B3's mutation, which the 29 facts first named for it did not.

- **2026-10-24, 5.7.1.** CI's Linux runner failed two facts on 5.7.0. Fact 17's "every part within a second" cannot
  be met where the browser takes 700 to 880 ms for one part alone: every part is now held to a second or to that cost
  for each part, one and a half times over. And 5.1.0's fact for the order of a toggle's drawings found a fault: every
  redraw makes the slider again (every drawn diagram has one since Z1), and `applyZoomLevel` held the browser's scroll
  anchoring off (Z6) even when nothing changed size, so through a toggle it was off most of the time, and a reader's
  jump down the page landed short by what the browser laid out late (content-visibility), 10,863 px on that fixture.
  On this machine it showed only with the processor slowed six times (CDP), in 5 runs of 13, and never on 5.6.1's
  scripts. The anchoring is held, and the place kept, only when a picture's or a part's width changes (`resizes`),
  far parts included; two mutations of it join the harness, 34 in all.

- **2026-10-24, 5.7.3, the audit.** The owner: "Can you make the plan was implemented in full and that no bugs were
  introduced as part of this session, and fix any bugs you find along the way." The plan was ticked off rule by rule
  against 5.7.2's code and facts, and the code read for faults. Every rule is in the code. The execution had left
  three things undone: 4.4's bench and 7.4's acceptance had never been run, and 6.4's list was one mutation short
  (only the part under the pointer resized).
  - **Acceptance (7.4)**, on the consumer report with 5.7.2's scripts (`probe/acceptance.js`, new), at 1920 x 1000 and
    1366 x 768. All 508 laid-out diagrams have a slider, and no part has one of its own (334 wide and 174 that fit at
    1920; 462 and 46 at 1366). The 24 diagrams in parts are each at one level, and lined up to 0.000 px by their
    dashed lifelines; no stored source holds a line the page added; the floor is 50 (5 notches of 5 prevented).
    Scenario 1's three parts: a wheel over any of them zooms all three, a click rings the box and not a part, and a
    second click lets go; the footbox is drawn once; the copied SVG parses, holds all 5,560 texts of the parts and one
    `plantuml-src`, and is as tall as the parts together. A notch keeps the point under the pointer to 0.43 px across
    and 0.16 px down (scenario 10, wide) and 0.48 px down (scenario 33, which fits, zoomed out). Scenario 278's slider
    stays in the window at every depth, and its PNG items say "part 2 of 15". A copy holds 64 ids twice within one
    picture: the engine's own (a message's group and its `msgN`), as diagrams drawn whole hold 60, not the page's.
  - **The bench (4.4)**: the toggles of the consumer report's longest feature (scenarios 277 and 278), three rounds
    alternating, 36 switches a version. 5.7.2 took 17% longer than 5.6.1 to finish a switch, and 3% with lining up off
    (`__kronikolLineUp = false`), and a profile put the cost after the parts were drawn, not in the outline: the new
    parts were lined up only once shown, and each part's margin written then laid it out again in the frames after.
    Sized while still hidden, 5.7.3 finishes a switch in 2,366 ms against 5.6.1's 2,177 (9%, inside the plan's 10%)
    and reaches the first picture in 945 against 916. A second profile, of a switch drawn from the cache, found every
    far part read once a frame after the toggle to find nothing to change (181 ms of forced layout); a part whose style
    holds the level is now passed over unread, and a third profile shows that layout paid once, by the slider made
    again, so the work is the same and the frames after a toggle are free of it. What is left over 5.6.1 is the layout
    of parts as wide as the whole diagram.
  - **Faults fixed**, each red first (`ZoomRobustnessTests`, and one fact in each of `PartsZoomDrawingTests`,
    `LinedUpPartsTests` and `ReportDiagnosticsTests`). From 5.7.0 to 5.7.2: the first frame of a toggle (B2); far
    parts read to find nothing to change; a diagram at its fit level after the window's width changes, near the screen
    and far from it; back to the fit level from a pan across; the popup's zoom scrolling the report behind it, a zoom out
    there undone every other notch (the popup is as wide as what it holds, so it narrowed with the picture, and the
    picture then measured as a wide one), and its activity diagram with no slider when drawn on a hidden tab; no picture items while a part will never draw; a
    diagram a hair wider than its box; a notch over the seam between two parts; Ctrl with a sideways wheel; Ctrl and
    minus typed in a field; an outline the engine could not draw kept for the session; a part too large shown with
    the engine's text (Z12). Older: a keyword-named sender at the top of a diagram (`queue -> billing`) taken for a
    declaration, so every part drew that message again, and since 5.7.0 the row went in after it (3.0.43 at the
    latest); parts after a cut at a long note numbered from 1 (3.0.43); a part the engine never answered never settled
    (3.0.45); a participant declared partway drawn under its alias in later parts (3.0.43), found by the Q7 fact once
    its fixture had the made participant in two parts; and a picture the server or the local Java engine drew, whose
    root says its height in pixels with `preserveAspectRatio="none"`, squashed across when drawn narrower than its
    width: at a wide one's fit level since inline SVG began, and zoomed out since 5.7.0.
  - **Facts made stronger**: the cursor of fact 5 and of ZoomKeptOnRedrawTests is read off the picture under a hovering
    pointer (the box's own style is empty either way); facts 9 and 12 check that the zoom out took effect; two facts join
    SplitDiagramZoomTests, a notch over a part that fits beside a wide one (fact 14's parts, lined up, are all as wide)
    and a drag that leaves an unselected diagram unselected; fact 20 checks where each part's picture
    stands in the copy; LinedUpPartsTests' facts 4 and 5 check that the parts are lined up, the second page of fact 6
    that it is not, and fact 7 that the engine's text decoded; the Q7 fact checks the made participant (named and in
    line in every part that has it). Fact 17's name says what it holds (fifteen parts or more, a budget per part).
  - **Narrowings, recorded**: `hide footbox` and lining up apply only to a diagram whose first part declares its
    participants (a generated diagram always does; a split activity diagram keeps its footbox, as it should). A
    participant made with `create` takes no gap from the row in the part that makes it, so where a later part's messages
    to it need more room than its head, it stands that much apart there. A declaration carried into later parts is
    written outside any `box` it stood in.
  - **The mutation run**: 56 mutations, one for each fix, each failing a fact. 6.4's missing one (only the part under
    the pointer resized) needed a fact of its own, every part on screen at the new level in the notch's own task
    (`PartsZoomDrawingTests`), as fact 17 is met by a notch that paints less. The first run on this release's code
    killed 42 of 55; nine did not build, a test file having been edited while it ran, and fail a fact when run again.
    Four survived, each naming a fact that did not reach its case: the numbering fact's note was capped under the cut
    by the payload's scroll box (the fact turns the box off, `PayloadScrollLines = 0`); the failed outline's fact drew
    other parts next (it now draws the very parts again); the hair-wide fact's parts had no shift (it now draws Q7's
    source, whose later part is shifted); and the popup fact read only the report behind the popup. Made to read the
    point in the popup, that last fact found the zoom out undone every other notch. Its survivor, holding the popup's
    own scroll anchoring off, changed nothing, and that code is gone: the browser anchors on the picture under the
    pointer or on what is above it. A far part's check that it changes size (`resizes`) now guards only a part
    restyled to its own width, which no fact draws, so its mutation gave way to one of the style check that passes a
    part over unread.
  - **Left as they are**: a picture item clicked before the menu has encoded the source (a few milliseconds) copies
    without `plantuml-src`; Q10's canvas limits are not measured per browser; the whole-diagram copy's background in a
    dark theme is not checked against a light one.

## Appendix A. Harness

`ZOOM_EVERY_DIAGRAM_PLAN.harness/`:

- `measurements.md`: M1 to M11, the method, the commands and every table.
- `mutate.js`: the mutation run of 6.4 (node): each mutation applied to a page script of a worktree, the E2E project
  built, the facts named for it run, the script put back; `--check` only finds each mutation's text. Edit no test
  file while it runs: its builds compile them.
- `probe/acceptance.js`: 7.4's acceptance, none of it timed, on a report with a worktree's scripts swapped in
  (`patch-report.js`): every slider, one diagram in parts, the point under the pointer, a tall diagram (the 5.7.3
  audit).
- `probe/probe-widths.js`: every drawn box of a report, its natural size, its box's width with and without padding.
- `probe/analyse.js`: the tables of M1, M3 and M5 from its output.
- `probe/probe-behaviour.js`: one scenario's boxes; the wheel kept or not, the text sizes, the move of the point under
  the pointer after one notch (M2, M3, M4).
- `probe/probe-parts.js`: one scenario's diagram in parts; each part's lifelines, a notch's layout for one part and
  for every part, and where a part's slider is once the page is scrolled into it (M6). `analyse.js`'s last line gives
  M6's table of parts drawn smaller.
- `probe/probe-twice.js`: every diagram in parts drawn again with the row, and an outline of each tried (M8).
- `probe/probe-compact.js`: every diagram in parts drawn once with the row sized from a compact outline (M10).
- `probe/probe-split.js`: every diagram in parts drawn in parts and whole, through the page's engine (M9).
- `probe/float-bench.js`, `probe/sum-toggles.js`, `probe/analyze-profile.js` and `probe/patch-report.js`: the 5.5.1
  work's toggle bench, its summary, its CPU-profile reader, and the tool that swaps a worktree's page scripts into an
  older report (M9: the owner's 5.4.4 report with 5.5.2's scripts).
- `probe/probe-align.js`: one scenario's parts drawn again through the page's engine with a row of hidden messages;
  each part's lifelines and shapes with and without it, the labels' calibration, and with `PROBE_CAP=1` how far one
  label pushes its gap (M7).
- `probe/probe-excess.js`: every diagram in parts as drawn, lined up and drawn whole, its extra width split into the
  left side, the span and the right side, with the outline's own numbers (`PROBE_TRUENUM=1`), the corrected row (on
  unless `PROBE_ROWFIX=0`) and each source changed one way (`PROBE_VARIANTS`) (M11).
- `probe/probe-idle.js`: how wide a lined-up diagram would be if a part left out its idle participants (M11).
- `probe/probe-notes-left.js`: every laid-out picture with each reply's note on the left of its arrow (M11).
- `probe/probe-newpage-cost.js`: chosen diagrams in parts drawn as today, whole, and cut with `newpage` (M12).
- `probe/probe-teoz-scaling.js`: made-up diagrams of growing length, with and without notes (M12).

The consumer report is not in the repository; it was written by 5.4.4, before 5.6.0 made a report page a loader
page, and the probes read a page of that kind. The probes take `PLAYWRIGHT_CORE`, the path of a `playwright-core`
package (the measurements used `C:/Code/Kronikol4J/playwright/node_modules/playwright-core`), and launch through
`tools/playwright/installed-browser.js`.
