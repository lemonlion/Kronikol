# TOGGLE_JUMP_PLAN: a control that draws diagrams again keeps the reader's place

**Status:** EXECUTED as 5.34.2 (2026-11-07), at the owner's word "can you implement the plan in full", every
question as recommended; what the build and an independent review of it changed is in section 9. Written by kronikol-8b at the owner's ask: "do a deep dive
investigation, and create a detailed, well researched plan to fix the underlying cause of issue 227". Checked again and
deepened the same day, at the owner's ask "make sure the plan is fully complete, accurate and then any deep dives are
done that might change it": the design was prototyped and measured in three browsers (section 3, M9 and M10), and five
of its rules changed (section 9). Covers #227 ("When clicking the toggle buttons on zoomed in reports they sometimes
jump you to a different place so that the diagram is off screen", v5.27.13). Measured on main at 896ddad3 (5.31.1) and
601c1dad (5.32.0); none of the scripts involved has changed since 5.27.13. Line numbers hold on 601c1dad. CM is
`src/Kronikol/Reports/context-menu-script.js`, CN is `src/Kronikol/Reports/collapsible-notes-script.js`. The
measurements, probes and the prototype are in `TOGGLE_JUMP_PLAN.harness/` (Appendix A).

## 0. In short

- **Reproduced in Chromium, Firefox and WebKit.** On a report of eight wide diagrams, the three above the one being
  read zoomed in, one click on Headers in the report's bar moves the text the reader was reading 1,886 px up the page
  in Chromium and Firefox, and 2,795 px in WebKit: the diagram is gone from the window (M4, M8). Nothing about it is
  random. "Sometimes" is where the reader stands and what is zoomed.
- **Two faults, both in how the page keeps the reader's place across a redraw.**
  - **A: putting a redrawn diagram's zoom back scrolls the page.** The redraw writes the new picture into the box, and
    the picture loses its zoomed width (CN L2696-2700). The zoom is then put back: `afterRedraw` (CN L3420-3421) calls
    `restoreZoomState` (CM L1624-1626), whose `addZoomButton` applies the level (CM L1598). The zoom code sees a width
    change and takes it for a reader's zoom (CM L1535-1545). It reads "the reader's place" from the new picture at its
    fit size and scrolls to keep it. Three effects:
    - a zoomed diagram far above the window scrolls the page by the 1,026 px its zoom gives back, though its scenario
      is not laid out (`content-visibility`) and its growth never reaches the page: M4's whole jump;
    - on a short page, the moment at fit size makes the page shorter and clamps the scroll: up to 255 px with nothing
      visibly changed (M1);
    - for the diagram being read, a place read on a picture the reader never saw: 6 to 74 px off (M8).

    A note's own buttons hit it too, with no control involved: folding a note of a zoomed diagram paints a frame with
    the diagram 239 px off before the note's own rule corrects it (M11).
  - **B: when the window's top edge cuts a diagram that is drawn again, nothing keeps the place.** The page leaves
    redraws to the browser's scroll anchoring. The browser anchors on what is at the top of the window, which is often
    inside the picture of the diagram above. The swap removes that picture, and the browser makes up for none of the
    height change. The diagram above grows 553 px under Headers, and the text being read moves 649 to 692 px down, to
    the window's foot (M3, M8). This happens with no zoom at all, in all three engines. WebKit has no scroll anchoring
    (CM L1318-1322).
- **Why "zoomed in" reports.** Fault A needs a zoomed diagram. Fault B is made likelier and larger by one: a zoomed
  picture is taller, so the window's edge falls on a picture more often, and it grows by more. The page zoom ("zoomed
  in" in the browser's sense) does the same, as the diagrams get wider than their boxes and taller than the window.
- **The fix (one patch), prototyped.**
  - The page keeps the reader's place itself across every redraw a control starts. It reads the place once, when the
    control is used: the drawn text nearest the middle of the window below the bar (Q1, Q4). It puts that text back
    after each swap, in the swap's own task, before the paint, and lets go when the reader scrolls.
  - A zoomed box's new picture is sized at its level as it is written, so putting the zoom back changes nothing and
    scrolls nothing.

  A prototype of both, built at runtime on what the scripts export, keeps the reader's text within 1 px in 46 of 48
  runs across 16 cases and three engines (M9). The other two are T9, a scenario's own toggle:
  - in WebKit the text was lost and the fallback kept it within 12 px;
  - in Chromium the page scrolled 6 px on the click before any handler ran, and the keeper kept a neighbouring text
    exactly, 28 px from the probe's.

  No other script wrote the scroll in any run.
- **What pins it today: nothing.** The 11 facts of `ZoomKeptOnRedrawTests` check the level and the place across; no
  fact anywhere checks where the reader's content stands after a redraw. S0 adds that class first, red on today's code
  by the numbers above (section 6).
- **Questions for the owner (section 8).**
  - Q1: which line is the reader's place. Recommended: the middle of the window below the bar.
  - Q2: one patch or two. Recommended: one.
  - Q3: whether a note's own buttons move onto the keeper now. Recommended: not in this release.
  - Q4: keep a drawn text or a fraction of the diagram. Recommended: the text, with the fraction where the text is
    gone.

## 1. The ask

The owner filed #227 on 2026-10-30 against 5.27.13 (.NET 10, xUnit v3) with the title only: "When clicking the toggle
buttons on zoomed in reports they sometimes jump you to a different place so that the diagram is off screen". On
2026-11-07 they asked for a deep dive and a plan to fix the underlying cause, then for the plan to be made complete and
accurate, with any deep dive that might change it.

Read here as:

- **"The toggle buttons"**: the report's diagram controls in its bar. That means Headers, Steps, Assertions,
  Databases, Events, Setup, the payload buttons, the line limit, the note format and the note width. Each draws every
  drawn diagram again through `renderWithPending` (CN L3900) into `processRenderQueue` (CN L3489); its six callers are
  `_setReportDetails` L3957, `_setTruncateLines` L3973, `_toggleHeaders` L4011, `toggleReportFilter` L4051,
  `_setNoteWidth` L4122 and `_setNoteFormat` L4149. A scenario's own controls in its settings bar do the same for that
  scenario: six handlers (CN L3934, L3986, L4018, L4060, L4131, L4156) call `show` (CN L4220), which draws through
  `redrawLater` (CN L4173). The Components tab's three controls (Show, Direction, Call summaries) draw its diagram
  again on their own (`report-components-script.js` L118-145).
- **"Zoomed in reports"**: a report where the reader has zoomed diagrams (the slider, Ctrl and the wheel, Ctrl and +).
  It could also mean the browser's page zoom. The plan covers both: the faults are the same at every page zoom, and
  1366 x 768, the window measured, is what a 1920 x 1080 screen shows at about 140%.
- **"The diagram is off screen"**: the diagram the reader was looking at leaves the window. M3 and M4 reproduce it both
  ways: pushed down to the window's foot, and thrown up out of the window.

## 2. What happens today

### 2.1 A redraw

`processRenderQueue` (CN L3489) sorts the diagrams by their distance from the screen and draws them one at a time.
For each one:

- **Whole picture, drawn by the engine (CN L3762-3786).** A box near the screen is frozen at its height
  (`minHeight`, CN L3764). The engine draws into a hidden child. Then `swapPicture` (CN L3778) writes the new picture,
  or the engine's failure text, into the box. The freeze is released (CN L3784) and `afterRedraw` (CN L3785) runs.
- **Whole picture, from the cache (CN L3753).** The same swap and `afterRedraw`, with no freeze.
- **Parts (CN L3605-3684).** The new parts are drawn hidden. The zoom is read (`zoomBefore`, CN L3656) and the old
  parts removed. `_sizeHiddenParts` (CN L3668, CM L1283) sizes the new parts at the diagram's level, then they are
  shown, the zoom is given back (`zoomAfter`), and each part gets `afterRedraw`.
- **A drawing that fails or times out** (`drawingFailed`, CN L2712) swaps nothing. The old picture stays and the
  state is put back.

`swapPicture` (CN L2696-2700) keeps the box's zoom state: the level, the mark and the place across (`_zoomState.of`
and `give`, CM L1812-1837). It does not keep the picture's zoomed width. That width is an inline style on the old
`<svg>`, and the new one does not have it. `afterRedraw` then puts the zoom back:

- at once, for every zoomed box near or far (`_restoreZoomState`, CN L3420-3421, CM L1624). It calls
  `addZoomButton` (CM L1626), which applies the box's level with `applyZoomLevel` (CM L1598);
- or a frame later, when the box fits its notes to their column (`_refineNoteWidth`, the full note width) or the
  page has no observer: `decorateNow` makes the slider in a frame (CN L3397).

The Components tab's diagram is swapped by its own code with the same `of` and `give`, and makes its slider a frame
later (`report-components-script.js` L140-145).

### 2.2 What keeps the reader's place

- **The browser's scroll anchoring**, for everything the page does not handle itself. It keeps a node near the top of
  the window in place when content above it changes size. Measured here (M3): when that node is inside a picture the
  redraw replaces, Chromium and Firefox make no correction for the change. WebKit has no anchoring at all (CM
  L1318-1322).
- **The zoom code's own place keeping (Z6 of `ZOOM_EVERY_DIAGRAM_PLAN.md`)**, for a reader's zoom.
  `applyZoomLevel` (CM L1527) asks `resizes` (CM L1475) whether the level changes a picture's width. If it does, it
  holds the browser's anchoring off for a frame and a task (`holdScrollAnchoring`, CM L1324), reads the place
  (`placeBefore`, CM L1348), and resizes, then scrolls the place back (`keepPlace`, CM L1375). The place is the point
  under the pointer, else the one at the window's middle (`innerHeight / 2`, the whole window's, CM L1350-1351). A box
  above the window keeps its bottom.
- **A note's own buttons** bring the note's new bottom to the click once the redraw is done, when the note shrank and
  the click was in its lower half (CN L396-416).
- Since 5.7.1 a slider made again at the level the picture already has changes nothing and keeps nothing
  (`resizes`), which fixed a toggle that held anchoring off for every diagram. Since 5.7.3 new parts are sized while
  hidden, so a diagram in parts has nothing to change either.

### 2.3 Where the two faults are

- **Fault A.** A whole picture drawn again has no zoomed width when its zoom is put back, so `resizes` says the level
  changes it. `applyZoomLevel` takes that for a reader's zoom: it reads the place from the new picture at its fit
  size and keeps it.
  - For the box being read, that is a point the reader never saw.
  - For a zoomed box above the window, it keeps the box's bottom: a scroll by all the height the zoom gives back.
    Where the box's scenario is laid out, the browser has already corrected the shrink to fit at the restore's first
    read, and the two add up to the box's real growth (M4, s2). Far above the window the scenario card is not laid
    out (`content-visibility: auto`, `stylesheets.css` L63-64), the growth never reaches the page, and the scroll is
    pure error, 1,026 px each.
  - On a short page, the forced layout at fit size clamps the window's scroll.
  - The same happens for a note's own buttons and the Components tab's controls, which take the same path.
- **Fault B.** Nothing keeps the place for a swap. The browser does, unless its anchor was inside the swapped picture
  (Chromium, Firefox) or it has no anchoring (WebKit).

## 3. Measured

Full tables, commands and fixtures: `TOGGLE_JUMP_PLAN.harness/measurements.md`. Window 1366 x 768.

The measure is the reader's content: the drawn text of the diagram read nearest the reading line before the click,
found again after by its words and which time they occur. The reading line is the middle of the window below the bar,
450 px. The box's own move is in brackets: a diagram that grows moves its top even when the reader's place is kept.

| | case (Headers in the report's bar unless said) | Chromium | Firefox | WebKit | writer |
|---|---|---|---|---|---|
| M1 | one zoomed diagram on a short page, 74% / 100% | +149 / +255 | +149 / +254 | +149 / +255 | keepPlace < applyZoomLevel < addZoomButton < restoreZoomState |
| M3 | the window's edge on the picture above, 74% | +664 (+553) | +664 | +664 | none |
| M3 | the same, not zoomed | +650 (+553) | +649 | +650 | none |
| M3 | the edge 40 px above that picture's bottom | +692 (+553) | +692 | +691 | none |
| M3 | the edge in the scenario's foot instead, not zoomed | +166 (0) | +166 | +166 | none: the browser keeps the edge (Q1) |
| M3 | the diagram read zoomed 74%, across the window's middle | -16 (-216) | -17 | -15 | keepPlace < applyZoomLevel |
| M3 | 1,117 px into the zoomed diagram read | -74 (-629) | -73 | -73 | keepPlace < applyZoomLevel |
| M4 | the three diagrams above zoomed 74%, the one read not zoomed | **-1,886** (-2,052) | **-1,886** | **-2,795** | `scrollBy(+1025.78)` < keepPlace < applyZoomLevel, once per zoomed diagram above |
| M5 | a diagram that fits, zoomed out to 75%, Steps | +43 (+96) | +44 | +47 | under applyZoomLevel's hold |
| T7 | 2,000 px into a zoomed diagram in parts | +22 (0) | +22 | +22 | none |
| T9 | the scenario's own Headers, its diagram zoomed, across the middle | +15 (-207) | +6 | -15 | keepPlace < applyZoomLevel |
| M11 | a note's own fold, zoomed 74% | a frame 239 px off, then +111 | | | keepPlace < restoreZoomState, then the note's rule |

- **M2, how fault A reads its place.** The new picture after the swap is 316 px tall at fit, where the zoomed one
  was 482. The forced layout clamped the scroll from 279 to 112. `placeBefore` found the window's middle 10.8% down
  the fit picture, where the reader had been 41.7% down the zoomed one. Re-zoomed, the picture is back where it was,
  and `keepPlace` scrolled by 183 + 0.108 x 482 - 384 = -148.78.
- **M4, swap by swap.** The zoomed diagram just above the window is kept right in Chromium and Firefox: the browser
  corrects its shrink to fit (-138) and the zoom code its regrow (+1,026), +888 in all, its real growth. Each of the
  two zoomed diagrams far above scrolls the page +1,026 for a growth the page never sees. WebKit loses all three.
- **M3, the focus is not it.** The filter bar's comment (`report-filter-bar-script.js` L93) records that Chrome
  anchors on a focused control. A click that leaves the focus on the body moves the diagram the same 553 px.
- **M9 and M10, the prototype.** `probe/proto.js` builds the plan's keeper at runtime on what the scripts export. It
  wraps `window._zoomState.give`, which both swaps call right after the new picture is written, and
  `window._sizeHiddenParts`. Every row of the table, run in the three engines:
  - Keeping the drawn text (Q4 (a)): the reader's text stays within 1 px in 46 of 48 runs. The other two are T9: in
    WebKit the text was lost and the fraction kept it within 12 px; in Chromium the page scrolled 6 px on the click,
    before any handler, and the keeper kept a neighbouring text exactly.
  - Keeping a fraction of the diagram (Q4 (b)): within 19 px, but 34 to 40 px in M5, where Steps adds rows unevenly.
  - Nothing but the keeper wrote the scroll in any run: no `keepPlace` from the zoom code.

  Each piece turned off (M10): without the anchoring hold the results are the same, so the plan drops it. P5 alone
  leaves fault B, and makes M3 mid and T9 worse than today (+200, +228).
- **M12, cost.** The text lookup takes 0.3 ms at most, 1.6 ms over a batch of eight diagrams.
- **Not measured: T8**, a diagram in parts above the reader, cut by the window's edge. It is fault B's mechanism with a
  parts swap, which T7 exercises for the diagram read itself; S0 measures it.

## 4. Design

### 4.1 Rules

- **P1. One place, read when a control is used.**
  - **When:** the page reads the reader's place at the reading line before the control writes anything. That is in
    `renderWithPending` (CN L3900), for the bar's six controls, and in one helper the six scenario controls go through
    before `show`. The Components tab's controls read it too.
  - **Not:** `show` itself, which the search's way to a match also calls (`report-search-hit-script.js` L276, L293)
    before it scrolls to the match. Not a note's own buttons (P7). Not `processRenderQueue`, which serves all of
    these.
  - **The line (Q1):** the middle of the window below the bar and any stuck settings bar. That is
    `(kronikol_freeTop(t) + innerHeight) / 2`, with `t` the element at the window's middle (`kronikol_freeTop` counts
    a stuck settings bar only above its argument, and throws without one).
  - **The place**, either:
    - inside a diagram's box (`zoomBoxOf`): the drawn text nearest the line, by its words, which time they occur in
      the box, and its offset from the line (Q4). Also how far down the box's pictures the line is, as a fraction,
      taken from the first picture's top to the last one's bottom;
    - elsewhere: the element at the line, skipping anything fixed or sticky, and its top.
- **P2. Put back after every swap.** After each picture a batch writes, the page puts the place back at the line. That
  means a whole swap (drawn, cached, or the engine's failure text) and a parts swap. It happens in the same task, after
  the box's zoom is put back and before the paint. For a box place, the text is found again in the box's new pictures:
  its words, the same time they occur. The text is believed only when it moved no further than the fraction's answer
  plus the box's own change of height (30 px of slack). Otherwise, or when it is gone, the fraction is used. For an
  element place, its top. It is done with one `scrollBy`, carrying the part of a pixel the window could not scroll, as
  `sizeParts` does (CM L1512-1514).
- **P3. The browser's anchoring stays on.** The keeper reads its place after the browser's own correction, at the same
  forced layout, so the two never add up. Measured with the hold turned off: the same results in Chromium and Firefox
  (M10). Anchoring also keeps doing its work between swaps, as content-visibility lays scenarios out late, which 5.7.1
  measured breaking a jump when it was held off.
- **P4. Let go when the reader moves.** The place is dropped on any of:
  - the reader's own wheel, touch, or scroll keys outside a field (the space bar on a button presses it and scrolls
    nothing);
  - a press on the window's scrollbar (a `mousedown` on the root);
  - the reader's own zoom: the slider, or Ctrl and + or - (Z6 then keeps the point under the pointer);
  - any scroll the keeper did not write: the window more than 2 px from where the keeper last left it, found right
    before the next picture is written. That covers a link or Back (`kronikol_nav`), the filter bar's settle, the
    search's jump to a match and find in page. It is found at the swap, not by the scroll's event a frame later, as a
    swap in between would put the place back first, and before the new picture is written, as the layout after it
    carries the browser's own correction (section 9).
  - the end of its batch.

  The end is the frame after the last queue that was running when the place was taken, and every queue started
  meanwhile, has drawn its last diagram and its decorations. After that, nothing moves the page.
- **P5. A zoomed box's new picture is sized as it is written.** `_zoomState.of` (CM L1813), which every swap calls
  before it writes, reads a zoomed box's content width with its level: it already reads the box's scroll there.
  `_zoomState.give`, which every swap calls right after, sizes the new whole picture at that level. It takes the
  picture's natural width from the width the engine writes on it, as `_sizeHiddenParts` does. So the first layout
  after the swap is at the right size. The restore then finds nothing to change: no hold, no place, no scroll, no
  clamp. Rules for the change:
  - a box at its fit level is not touched or read, as its picture fits by default;
  - a zoomed-out box uses the width it keeps (`_kContentWidth`), as `factsOf` does;
  - a picture without a pixel width is left to today's restore.

  It covers the report's whole swaps, the Components tab's swap and a note's own redraw, none of which needs a change
  of its own. For parts, `_sizeHiddenParts` takes the width from the zoom state read before the old parts were
  removed (F6).
- **P6. Batches that overlap share one place**, the first one's, until it is let go: a second control used while the
  first is drawing keeps where the reader was before the first.
- **P7. A note's own buttons keep their rule** (CN L396-416), Q3. P5 removes the jump before it (M11).

### 4.2 Code

- **CM, a page-level place keeper** next to `placeBefore` and `keepPlace`. It uses `zoomBoxOf` (CM L26) and
  `picturesOf`. It is `window.kronikol_readingPlace = { take, check, keep, start, end, held }` (as built; the plan
  first named `take`, `keep` and `end`, section 9):
  - `take` reads P1's place if none is held;
  - `check`, called right before each picture is written, lets the place go when the window was scrolled since the
    keeper last left it (P4);
  - `keep` puts it back (P2), at once, with nothing to do when no place is held;
  - `start` and `end` bracket each redraw: the queue's run, a note-width pass waiting to draw, the Components tab's
    redraw. The place is dropped a frame after the last `end` (P4), and a control that started no redraw drops it the
    next frame;
  - `held` says whether a place is held, for the page's tests.

  The listeners for P4 go on the window once, in capture, passive. The keeper remembers the scroll it left the window
  at, so its own writes are never taken for anyone else's.
- **CM, `_zoomState`**: `of` keeps a zoomed box's content width in the state; `give` sizes the new whole picture
  from it and the `<svg>`'s width attribute, with `levelFor`, `sizeWhole` and `setZoomMarks` (P5). No `getClientRects`
  and no layout read in `give`.
- **CM, `_sizeHiddenParts(container, parts, zoom)`**: the content width from `zoom` when it has one (F6).
- **CN, the entries**:
  - `take()` at the top of `renderWithPending`;
  - one helper, `showFromControl(scenario, states)`, that takes the place and calls `show`, used by the six scenario
    controls (L3934, L3986, L4018, L4060, L4131, L4156).
- **CN, `processRenderQueue`**:
  - `check()` before each swap writes its picture (`swapPicture`, and the parts swap before it takes the old parts
    away), and `keep()` after each swap and its `afterRedraw`, at the three swap sites (CN L3656-3684, L3753, L3778);
  - `start()` when a queue begins and `end()` once it has drawn its last diagram and its decorations (CN L3545).
- **CN, the note-width pass** (`refineNoteWidth`, Note width Full): a pass that waits for a drawing to end is a
  `start()` and an `end()`, so the redraw it starts is kept too.
- **CN, the parts swap**: passes `zoom` to `_sizeHiddenParts` (CN L3668).
- **`report-components-script.js`**: its three controls call `take()`. Its redraw is a `start()`; its swap
  (L140-145) calls `check()` before it writes and `keep()` after `give`, and `keep()` and `end()` after the frame
  that makes the slider.

### 4.3 Performance

`take` is one `elementsFromPoint`, one pass over the texts of the box at the line, and one or two rectangle reads,
before anything is written. `keep` finds the text again in its box (0.3 ms at most, 1.6 ms over a batch of eight
diagrams, M12) and reads one rectangle, in a task whose `afterRedraw` already lays the page out (`restoreZoomState`,
`tagShown`, `zoomFacts`). P5 removes a forced layout at fit size, a hold and a scroll from every zoomed box drawn
again. Budgets that must hold, each run before and after:

- `ReportToggleResponsivenessTests` (14 facts), among them no long task, the paint within budget, and
  `A_toggle_draws_the_diagram_on_screen_first_and_one_scrolled_to_next_...` (L180). That last one is 5.7.1's order
  fact, also run locally with CDP `Emulation.setCPUThrottlingRate` x6, as 5.7.1 did;
- the 5.5.1 long-diagram paint fact
  (`A_toggle_with_a_long_diagram_of_many_notes_on_screen_runs_no_long_task_and_paints_the_new_picture_first`);
- `LinedUpPartsTests`' toggle budget, which is flaky as it stands (#234), so it is compared over five runs each side.

### 4.4 What changes for a reader

- After any control that draws diagrams again, the text that was at the middle of the window below the bar is still
  there, within a pixel. That holds however many diagrams above it change, are zoomed, or are cut by the window's
  edge, in every browser.
- A diagram that grows or shrinks itself does so around that text: its top moves where it grew above the line. Today
  the browser keeps the top of the window instead, where it can (M3 gap, Q1).
- The level and the place across are kept as since 5.2.3.
- If the reader scrolls while the diagrams are still being drawn, the page goes where they scroll and is not pulled
  back.
- A note's own fold or expand on a zoomed diagram no longer flashes the diagram off its place before the note's rule
  places it.

### 4.5 Alternatives

- **Turn the browser's anchoring off on pictures** (`overflow-anchor: none` on each `<svg>`). The browser would then
  anchor on the box. A box cut by the window's edge still grows below the edge, over what is being read, and WebKit
  has no anchoring. Rejected: it fixes neither fault.
- **Keep a fraction of the diagram only (Q4 (b)).** Simpler, and measured within 19 px in most rows, but 34 to 40 px
  where a toggle adds rows unevenly (M5), and it cannot follow a diagram drawn another way (the Components tab's
  Direction).
- **Fix fault A alone (P5).** Measured (M10): M3's fault B rows do not change, and the diagram being read across the
  middle moves more than today (+200 against -16), as nothing then keeps a place the browser cannot anchor.
- **Make the restore keep no place (a `restore` flag through `applyZoomLevel`).** The first plan's P6. With P5 no
  restore finds anything to change (M9: no `keepPlace` in any run), so the flag would never act; dropped.
- **Hold the browser's anchoring off around each swap.** The first plan's P3. Measured unnecessary (M10), and holding
  it off is what broke 5.7.0's jumps; dropped.
- **Redraw only diagrams near the screen.** The owner rejected this in 5.1.1: a jump across the page then shows the
  old diagram and a size change.

## 5. Bugs found on the way

| | what | measured | fixed by |
|---|---|---|---|
| F1 | Fault A: putting a zoomed diagram's zoom back after a redraw scrolls the page by a place read from its picture at fit size; a zoomed diagram far above the window scrolls it by a growth the page never sees | M1, M2, M3, M4, M8 | P5 |
| F2 | Fault B: a swap that removes the browser's anchor moves the page by the picture's height change | M3, M8 | P1, P2, P4 |
| F3 | On a short page the moment at fit size clamps the scroll, so even a redraw that changes nothing moves the page | M1 (+255) | P5 |
| F4 | A diagram that fits, zoomed out, jumps too | M5 (+96) | P5, P2 |
| F5 | WebKit (Safari) has no scroll anchoring, so a redraw above the reader moves the page there wherever the window's edge is, zoomed or not | M3, M4, M8; CM L1318-1322 | P1, P2, P4 |
| F6 | `_sizeHiddenParts` reads the box's width after the old parts are removed, so a box far from the screen, which has no height freeze, is laid out empty for that read | read, CN L3656-3668 | P5 |
| F7 | No fact checks where the reader's content stands after a redraw | `ZoomKeptOnRedrawTests` (11 facts) checks the level and the place across only | S0 |
| F8 | A note's own fold on a zoomed diagram paints a frame with the diagram off its place (239 px) before the note's rule corrects it 30 ms later; its other buttons take the same path (not measured) | M11 | P5 |
| F9 | The Components tab's redraw puts a zoomed diagram's zoom back the same way, a frame after its swap | read, `report-components-script.js` L140-145 | P5, and P1 for its controls |

Observed, not a fault: a note's own place rule corrects after the paint, as it polls every 50 ms until the drawing
ends (CN L403-407), so the reader can see one frame before the correction. That is Q3.

## 6. Tests

New class `ReadersPlaceOnRedrawTests` (`tests/Kronikol.Tests.EndToEnd`). Its fixture is the harness's `wide-many`
moved into `ReportTestHelper` as `GenerateReportWithTallWideDiagrams(tempDir, outputDir, fileName, scenarios,
calls)`. It is small enough for CI's Linux runner: eight diagrams of 40 calls, sequence only (the 5.1.1 lesson).

It runs in CI's "E2E (Diagram Interactions)" lane: added to that lane's filter and to E2E (Remainder)'s exclude list
(`.github/workflows/ci.yml` L123 and L140), as Remainder is the longest lane. Every fact uses the control with the
mouse. It waits for the batch to end (`!_noteRendering` on every box, no queue, two frames), with
`PollingInterval = 200`.

The measure is the probe's: the drawn text nearest the reading line before the click, found again after by its words
and which time they occur. "Green" is that text within 2 px of where it stood, the diagram read on screen, and no
scroll written by the zoom code during the batch.

### 6.1 S0, red on today's code

| fact | case | red today (Chromium, M8) | green |
|---|---|---|---|
| T1 | the window's edge on the picture above, nothing zoomed, Headers | +650 | the text within 2 px |
| T2 | the same, the diagram being read zoomed in | +664 | the same |
| T3 | the diagram being read zoomed in, across the window's middle; and 1,117 px into it | -16; -74 | the same |
| T4 | the three diagrams above zoomed in, the one read not | -1,886, off screen | the same |
| T5 | one zoomed diagram on a short page, a toggle that changes nothing on it | +149 (74%), +255 (100%) | 0 |
| T6 | a diagram that fits, zoomed out, Steps | +43 | the text within 2 px (the fraction alone is 40 px off: this fact pins Q4) |
| T7 | the reader 2,000 px into a zoomed diagram in parts, Headers | +22 | the text within 2 px |
| T8 | a diagram in parts above the reader, cut by the window's edge | measured by S0 | the same |
| T9 | a scenario's own Headers toggle, in its settings bar, on its zoomed diagram | +15 | the same |
| T13 | a note's own fold on a zoomed diagram | a painted frame 239 px off before the note's rule | no frame further off than the page's end allows (+111 in M11) |
| T15 | the window's edge in the scenario's foot below the picture above, nothing zoomed, Headers | +166 (the browser keeps the edge) | the text within 2 px (under Q1 (a)) |
| T16 | the Components tab's Call summaries on a zoomed component diagram taller than the window | measured by S0 | the same |

### 6.2 Guards, green before and after

- **T10.** The reader scrolls with the wheel while the batch draws: the page ends where the wheel took it (P4).
- **T11.** A jump down the report while a toggle draws lands where it aims: 5.7.1's order fact, plus the CPU x6 run.
- **T12.** Back after a toggle lands where `kronikol_nav` wrote it (`BackForwardRestoreTests`; #235 is its known
  WebKit flake).
- **T14.** No `scrollBy` from `applyZoomLevel` during a redraw: the page writes the scroll only from the keeper (a
  probe-style wrap of `scrollBy`, as the harness does).
- **T17.** The search's way to a match (`kronikol_diagrams.show` and `openNote`, then its own scroll) lands on the
  match: the keeper takes no place for it.
- The existing classes stay green: `ZoomKeptOnRedrawTests`, `ZoomRobustnessTests`, `SplitDiagramZoomTests`,
  `PartsZoomDrawingTests`, `GradualZoomTests`, `FittingDiagramZoomTests`, `ZoomNoteInteractionTests`,
  `LongPayloadScrollTests`, `FocusClearOfTheBarTests`, `ReportToggleResponsivenessTests`, `LinedUpPartsTests`, the
  five `BackForward*` classes, and the classes of the search's way to a match and the diagram API (`SearchHitTests`,
  `SearchHitWayTests`, `DiagramApiTests`).

### 6.3 Proofs

- **Browsers.** T1 to T9 also run in Firefox and WebKit locally, as CI installs Chromium only. The harness's
  `probe/rows.js` reruns every row of M8 on the release build, with the text within 2 px in all three.
- **Mutations.** Each must turn a fact red:

  | mutation | fact |
  |---|---|
  | `keep` skipped | T1, T2, T4 |
  | `take` after the first swap | T1 |
  | the text ignored, the fraction only | T6 |
  | the text believed without its bound | a fact with a toggle that removes the text's row (Databases, S0 picks it) |
  | P4's wheel listener dropped | T10 |
  | P5 skipped | T13, T14 (M10: the zoom code's `keepPlace` runs again) |
  | P5's width read after the swap | T13 (the clamp at fit size) |
  | the end not waiting for overlapping queues | a second toggle while the first draws (P6, S0 writes it) |

  The take moved into `show` is not caught by T17, as the search scrolls only after its redraw is done. T17 is a
  guard; P1's entry points are checked by reading.

  **As run on the release** (`TOGGLE_JUMP_PLAN.harness/execution/mutate227.py`, 31 mutations, each built and run
  against its facts; logs `mutations3.log` and `mutations4.log`). Caught, 22:

  | mutation | caught by |
  |---|---|
  | `keep` skipped | T1 in three engines, T2, T4 in three engines |
  | `take` after the first swap | T1 in three engines |
  | the fraction only | T6 and Review 4 |
  | the wheel listener and `check` both dropped | T10 |
  | `check` dropped | `ReportToggleResponsivenessTests`' 5.7.1 order fact |
  | P5 skipped | T3, T4, T13 |
  | the end not waiting for overlapping redraws | P6 |
  | the parts swap's keep dropped | T7 |
  | the scenario controls' take dropped | T9 both |
  | a take in `show` | T17 (section 9: it can after all) |
  | the texts drawn once ignored | T18, three of its four |
  | the farther side asked however far | T18's deep case |
  | the Components tab's take dropped, for Direction | Direction |
  | each review fault put back (1 to 4, 6 twice, 7, 9, 10) | its own Review fact |

  Survived, 8, each read and kept:

  | mutation | why |
  |---|---|
  | the wheel listener dropped | `check` lets the place go on the wheel's scroll anyway |
  | P5's width read after the swap | a box has the same width before and after in every fixture |
  | a text drawn once believed only within the fraction's bound | Direction's own change of height admits the component's move |
  | only the nearest text drawn once kept; the three nearest whichever side; only the nearer side asked; any drawing within 40 px | T18 passes with each: no fixture tells the rule as built from these, which the review's eighth point and the deep dive argue for |
  | the Components tab's take dropped, for Call summaries | P5 alone keeps T16's place |

  The text believed without its bound has no fact: no fixture leaves the text read without a text drawn once within
  half a window, so the bound is checked by reading.
- **The red runs are kept.** Each S0 fact's red number on today's code goes in the log (section 9), as section 6.1's
  third column does now from the probes.

## 7. Releases

### 7.1 Order and version

One release (Q2), a **patch**: it fixes behaviour and adds nothing a consumer calls or sets. `kronikol_readingPlace`
is the page's own, like `_zoomState`. The plan names no number: it is the next free patch when the release is pushed,
claimed with peers just before the push, as other sessions release meanwhile. It is done in a worktree of its own.
S0's red facts come first and are committed with the fix in one release commit. Released as 5.34.2.

### 7.2 Where

- CM: the keeper, `_zoomState.of` and `give` (P5), `_sizeHiddenParts`' width.
- CN: `renderWithPending`'s take, the scenario controls' helper, `processRenderQueue`'s keeps and queue count, the
  parts swap's call to `_sizeHiddenParts`.
- `report-components-script.js`: its controls' take, its swap's keep and end.
- Tests: `ReadersPlaceOnRedrawTests`, `ReportTestHelper.GenerateReportWithTallWideDiagrams`, `ci.yml` L123 and L140.

### 7.3 Documentation

- `CHANGELOG.md`: under Fixed, the two faults as a reader sees them, and "Patch: bug fixes only, nothing new to call
  or set".
- Wiki `PlantUML-Browser-Rendering.md`, "Zoom kept when a diagram is drawn again (5.2.3)" (L197-203 at 0ffd08b): a
  paragraph saying, from that version, that a control drawing diagrams again keeps what was at the middle of the
  window in place, in every browser. Edited in a wiki worktree of the release's own, from a private ref.
- Kronikol4J divergence ledger: one line, as the report's embedded scripts change (CLAUDE.md: a report output change
  is recorded there and does not raise the bump).
- No README change; no option, so no Report-Configuration change.

### 7.4 Acceptance

- Every row of M8 within 2 px of where the reader's text stood, in Chromium, Firefox and WebKit (`probe/rows.js` on
  the release build).
- T1 to T17 green; the classes in 6.2 green; the full Playwright suite green (announced to peers before it runs).
- The section 4.3 budgets met.

## 8. Questions for the owner

- **Q1. Which line is the reader's place?**
  - (a) **The middle of the window below the bar (recommended).** Whatever happens above or around it, the diagram
    covering the window's middle stays on screen, which is what #227 asks. A reader's own zoom keeps nearly the same
    point today: the whole window's middle (Z6), 66 px higher with the bar pinned; this plan leaves Z6 as it is.
  - (b) The top of the window below the bar, which is what the browser itself keeps. A diagram cut by that line that
    grows pushes what is below it down, as far as it grows below the line: the diagram being read can still leave the
    window. T15 would then be green today.
  - (c) The pointer's last place over the report, else (a). The pointer is on the bar when a report toggle is clicked,
    so this is (a) for every report toggle, with a second rule to test.
- **Q2. One patch (recommended), or fault A and fault B as two patches?** They change the same lines, and S0's facts
  cover both. Measured (M10): P5 alone leaves fault B and moves the diagram read across the middle further than today.
- **Q3. A note's own buttons:** keep their rule as it is (recommended for this release), or move them onto the
  keeper. The keeper would make their correction before the paint rather than about 50 ms after it, with the place
  read from the note clicked. It changes a rule tuned in 5.0.0 (`OPTION_C_PLAN.md` R17.9) that #227 does not ask
  about. P5 removes the jump before it either way (M11).
- **Q4. What the place is inside a diagram.**
  - (a) **The drawn text nearest the line, with the fraction where the text is gone (recommended).** Measured within
    1 px in 46 of 48 runs; of the other two, one was taken by the fallback (12 px) and one is the probe's own measure
    (M9); 0.3 ms a lookup (M12).
  - (b) A fraction of the way down the diagram. Simpler; within 19 px in most rows, 34 to 40 px where a toggle adds
    rows unevenly (M5).

## 9. Log

- **2026-11-07 (kronikol-8b), the plan.** Investigated and written in the worktree `C:/Code/Kronikol-227` (branch
  `plan/227-toggle-jump`, from 896ddad3). Fixtures were written with a probe class in the worktree only, its source
  kept as `TOGGLE_JUMP_PLAN.harness/fixtures-writer.cs`; the probes are in `TOGGLE_JUMP_PLAN.harness/probe/`. Ruled
  out:
  - the focus on the toggle (M3);
  - the focus script, which acts on Tab only (`report-focus-script.js`);
  - the filter bar's restore, which acts only when the scenario count changes (`report-filter-bar-script.js`
    L108-118);
  - the narrow bar (M7).
- **2026-11-07 (kronikol-8b), checked and deepened.** The branch moved onto 601c1dad (5.32.0); no cited script or CI
  line changed, and every line number was read again. The design was prototyped (`probe/proto.js`) and run on every
  row in three engines, today and with each piece off. What changed:
  - **Fault A's mechanism (M4).** The worst case is the zoomed diagrams far above, whose growth the page never sees,
    not "on top of the browser's own correction": the one just above the window was kept right.
  - **The measure.** The reader's text, not the box's top: the box of a growing diagram moves by design (M3 mid:
    -216 for the box, -16 for the text).
  - **The design.** The place is a drawn text with the fraction as its fallback (Q4, new). P1 reads it in the
    controls, not in `processRenderQueue`, which also serves the note buttons and the search. P3 no longer holds the
    browser's anchoring off: measured unnecessary. P5 moves into `_zoomState`, which also covers the note buttons (F8, new) and
    the Components tab (F9, new). The first P6, a restore that keeps no place, is dropped as P5 leaves it nothing to
    do.
  - **Facts.** A failed drawing swaps nothing, so it needs no keep. `ReportToggleResponsivenessTests` has 14 facts,
    not 6. The measured cases add T7 (+22), T9 (+15) and M11, and T13 is red today; new facts T16 and T17.
  - **The probe.** The first "deep" number (-844) came from a placement that `content-visibility` misled, and the
    probe now refuses it.
- **2026-11-07 (kronikol-8b), executed.** At the owner's word "can you implement the plan in full", every question as
  recommended: Q1 (a), Q2 one patch, Q3 the note buttons unchanged, Q4 (a). Built in `C:/Code/Kronikol-227` on 5c6b6f39
  (5.32.1), which changed none of the cited scripts. What the build changed from section 4:
  - **The keeper's API** is `{ take, check, keep, start, end, held }`. The count of running redraws lives in the keeper,
    bracketed by `start` and `end`, so that the queue, the note-width pass and the Components tab share it; the plan put
    a count in CN alone, which the Components tab could not see.
  - **P4's scroll rule moved to the swap.** The plan let go on a scroll event that found the place off its line. A
    scroll event comes a frame after the scroll, and a swap in between (the queue swaps several pictures a frame from
    its cache) put the place back first, pulling a link's or a test's jump back; the 5.7.1 order fact jumps exactly so.
    `check` now asks, right before each picture is written, whether the window moved since the keeper last left it.
    Reading the scroll forces a layout, so it is read before the write: after it, the layout carries the browser's own
    correction for the swap.
  - **More let-go:** the reader's own zoom (the slider, Ctrl and + or -), as Z6 then keeps the point under the pointer;
    and the space bar on a button presses it and lets nothing go.
  - **The note-width pass** (Note width Full draws a diagram again once painted, `refineNoteWidth`) is held for: a
    pass that waits for a drawing to end is a `start` and an `end`.
  - **The landmark tells repeated words apart (T18, a deep dive during the build).** A report's notes draw the same
    words again and again: each JSON field's name, each brace. The text nearest the line was often such words, and
    Databases or Setup, which take rows away above the line, changed which time they occur: the k-th time was another
    row, and the page was kept 207 px off on the dense report's database calls, though the probe's own measure (the
    same k-th time) said 0. The nearest text whose words the diagram draws once, 23 px from the line, showed it. Now the
    place also holds such texts within half a window of the line, the three nearest the text read above it and the
    three nearest below (the review's eighth point: with the nearest alone, rows a toggle adds between it and the text
    read, as Headers adds header rows, left the text off by their height). The text read is found again as a drawing of
    its words standing where one of them, still drawn once, puts it, to 3 px (2% of the way between them, for a picture
    drawn at another scale). The nearest still drawn once on each side is asked, the nearer first, and the farther only
    as far as twice the nearer's way and 20 px more; agreeing with neither, the nearer is kept at its own offset; the
    k-th time only with none of them left. Two builds before it were wrong on T18's deep case, 449 px off, which the
    `t18-probe.js` of the execution logs showed: the note read was a database call's, which Databases takes away, so
    the nearest text drawn once (`When`, 36 px below) agreed with nothing; asking every other one, the best agreement
    of six within 40 px and then the first within 3 px, found a `...` where a text 170 px above said, in the next note
    of the same customer's address. The probe gained the measure (`unique` in `probe/jump.js`'s landmark).
  - **T17 can catch a take moved into `show`.** It asks `held()` right after the diagram API's `show`, so the
    mutation is caught after all (section 6.3 said no fact could).
  - **The facts click with the mouse at the control's middle** (`Mouse.ClickAsync`). A locator's click scrolls the
    page to bring the control into view first, 6 px for a scenario's settings stuck under the bar: that was the 6 px
    "on the click before any handler" of M9's T9 row, the probe's, never a reader's.
  - **T16** hides the call summaries first and shows them in the measured click, so the text read is a component's
    name, which both drawings have; with the controls in the window, as a reader has them. A second Components fact,
    Direction, keeps the component read where the diagram is drawn anew (a fraction cannot); its report is eight
    chains of ten, as twenty of six fit the window drawn top to bottom and left the page nothing to scroll.
- **2026-11-07 (kronikol-8b), an independent review of the fix.** A read-only agent, given the code and not this
  plan's conclusions, found eleven faults. Each has a fact, red on the first fix and green on the second
  (`ReadersPlaceOnRedrawTests`, "Review n"), save the fifth, which is work saved:
  1. **A text such as `constructor` stopped the redraw.** The engine draws each line of a label or a note, and each run
     of markup in it, as a text, and the keeper looked the drawn texts up in a plain object, whose `constructor` and
     `toString` are its own: a label's line that reads `constructor` threw after the first picture written, the queue
     stopped with its controls busy, and the place was never let go. The texts are kept in an object with no
     prototype. (The review took each word for a text of its own; a label's words are one text, so the fact draws the
     word as a line of its own.)
  2. **A place taken out of the layout ran the page away.** The place on what no diagram draws (a scenario's title, an
     outline row's steps) read its top as 0 once hidden (another example row chosen, a tab switched), and was put back
     from there after every picture written. A place with no box now cannot be told, and nothing is kept for it.
  3. **A second control kept a place the page had left.** `take` kept a held place without asking: after a link's
     jump and before the next picture, the second control's diagrams kept nothing. `take` now checks the held place
     first, and takes a new one when the window moved from it or it cannot be told.
  4. **P5 forgot a zoomed-out box's width after the window changed width** (the resize handler drops it, and nothing
     reads it again while the box stays zoomed out): the new picture was written at full size. `of` reads the box's
     width when its remembered one is gone.
  5. **Work after every picture.** Each keep gathered the words of the whole diagram read, after every picture any
     diagram wrote. The text found now stands while the diagram's pictures are the same objects, seeded at the take,
     and is looked for again only after the diagram read was drawn again.
  6. **Too much let the place go:** a wheel across a diagram or inside a scroll box of its own, and a tap. Now a wheel
     that scrolls the window down or up (or zooms, with Ctrl or the command key), and a finger that moves.
  7. **CSS that scrolls smoothly** (`scroll-behavior` in a report's CustomCss) animated every scroll the keeper wrote,
     and its next check took the animation for someone else's scroll. The same held, before #227, for the zoom code's
     own place keeping (a notch on a diagram in parts scrolled again by what the animation still had to go,
     `PartsZoomDrawingTests`), the pan and a note's fold. Each such scroll is now written with `behavior: 'instant'`.
  8. **One text drawn once on one side** (above): see the landmark above.
  9. **The browser's own anchoring** scrolls the window with what it keeps in place, which leaves the place on its
     line; `check` took it for someone else's scroll. It now lets go only when the window moved and the place moved
     off its line.
  10. **The Components tab's keep a frame after its swap** asked nothing first, and could undo a scroll made in between
      (Back's settle). It checks first.
  11. **The facts' measure** (the k-th time of the words) misreads a filter that takes rows away: T18's measure is the
      nearest text drawn once, and T18 now covers Events as well. The dense report draws no assertion notes, so its bar
      has no Assertions toggle to cover.

  Three of the first facts were not red on the first fix as written, and were made to be. A label's words are one
  text, so the first fact draws `constructor` on a line of its own. The wheel and the tap came a moment after the
  click, when the nearest diagrams were drawn already and nothing was left to keep, so they are dispatched in the
  click's own task. A smooth scroll ends where it should, so that fact also asks every frame. The second toggle after a
  jump first timed out on both fixes, as the second Headers returned the diagram read to the drawing it still had; it
  is now clicked right after the first click's picture of it is written.
- **Red, on 5.32.1's scripts with the final facts** (Chromium unless said): T1 +649.7, Firefox +649.8, WebKit +649.7;
  T2 +664.2; T3 -16.0 and -74.0; T4 -1,885.9, Firefox -1,886.1, WebKit -2,794.9, the diagram read off the screen; T5
  +149.0 and +255.0; T6 +43.5; T7 +22.2; T8 +225.2 (the diagram in parts above grew 87 px); T9 +6.0 and +14.0; T13 a
  frame at +239 before +111; T15 +166.3; T16 -266.0; the two clicks of P6 -138.0; T18 -246, -281 and -246; Direction
  -262. T10 and T17, the guards, pass. Every number is the plan's measured one where the plan has one.
- **Red, on the first fix, the review's facts** (`test-red-review2.log`): Review 1, the redraw queue stopped (the fact
  timed out at 120 s); Review 2, the keeper scrolled the page 8 times after its place had left the layout; Review 3
  -193.8; Review 4, the zoom code scrolled the page (`keepPlace` under `applyZoomLevel`); Review 6, the wheel +664.2
  and the tap +649.7; Review 7, 23 frames off its place, up to 650 px; Review 9 +96.7; Review 10, the page's own 400 px
  scroll undone (the text read at 0.0); `PartsZoomDrawingTests`' smooth notch 12.9 px off. T18's deep case was red on
  the two builds between (-449.0 both).
- **Green, on the release** (run on 5.34.0, which changed none of these scripts, and `ReadersPlaceOnRedrawTests` again
  on 5.34.1, which changed how a diagram draws its participants: 36 of 36): `ReadersPlaceOnRedrawTests` 36
  of 36 and `PartsZoomDrawingTests` 6 of 6, in three engines where the facts run them; the guards of section 6.2, 357
  (one failed once on a temporary folder another test run took away, and passed alone, 49 of 49);
  `ReportToggleResponsivenessTests` again, 14 of 14; `LinedUpPartsTests` five times on each side, its fifteen-part
  toggle 43 s three times on a quiet machine on the fix and 41 to 43 s on 5.34.0's scripts (42 to 56 s on the fix
  while another session's Linux container ran); the whole end-to-end suite, 2,191 passed and 28 skipped; the core
  suite, 12,592 with its three script-text facts moved to `showFromControl` and the pan's instant scroll. The disk
  filled for a few minutes during the mutations (another session's build), and the eight mutations it broke were run
  again. Logs: `TOGGLE_JUMP_PLAN.harness/execution/`.

## Appendix A. Harness

`TOGGLE_JUMP_PLAN.harness/`:

- `measurements.md`: M1 to M14 in full, with the command for every row.
- `probe/jump.js <fixture> <toggle> <zoom> [where] [scenario] [vw] [vh]`. It draws every scenario and zooms the
  scenario's diagram (`in` halfway up its range, `max`, `none`). It places it with `mid`, `top`, `cut`, `gap`,
  `below-mid` or `deep:<px>`, retried until it holds. It then uses the report's toggle with the mouse, or
  `toggle=note` (a note's fold, double-clicked). It prints the per-frame samples, the reader's text before and after,
  and every scroll write with its stack. Environment:
  - `BROWSER=chromium|firefox|webkit`;
  - `SCOPE=scenario` (the scenario's own toggle, its settings opened);
  - `CLICK=js|blur` (a click that leaves the focus on the body, or a mouse click then a blur);
  - `ZOOM_ALSO=i,j` (zoom other scenarios first);
  - `PROBE_RECTS=1` (log every rectangle the zoom code reads);
  - `TRACE=1` (log every swap and zoom restore with the scroll and the diagram read before and after);
  - `PROTO=1` (put the prototype in first); `PROTO_MODE=landmark|fraction`; `PROTO_OFF=take,keep,hold,p5,let`.

  The bundled Chromium only, never a channel (CLAUDE.md).
- `probe/proto.js`: the prototype of section 4, as the probe puts it in.
- `probe/rows.js [row ...]`: runs M8's rows through `jump.js` and prints one line each.
- `probe/summ.js` and `probe/summ-point.js`: print a `jump.js` result compactly. `probe/narrow.js`: where the
  toggles are at a width.
- `fixtures-writer.cs`: the probe class that writes `fixtures/` (not committed, about 2.2 MB). Copy it into the E2E
  project, run it with `--filter "FullyQualifiedName~Probe227Fixtures"`, and delete it again.
