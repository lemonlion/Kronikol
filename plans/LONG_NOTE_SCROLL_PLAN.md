# LONG_NOTE_SCROLL_PLAN: a long payload scrolls inside its box

**Status: plan written, nothing implemented, NOT green-lit.** Written 2026-10-21 at the owner's ask (section 1), and
revised the same day after an independent review (section 10). Every `file:line` is at `origin/main` `4a0dad9e` (5.2.2),
under `src/Kronikol/` unless it says otherwise; CN, RS, CM, IS, KN, RES, RG and PC are `Reports/collapsible-notes-script.js`,
`Reports/plantuml-browser-render-script.js`, `Reports/context-menu-script.js`, `Reports/inline-svg-styles.css`,
`Reports/report-keyboard-navigation-function.js`, `Reports/ReportToggleDefaultsResolver.cs`, `Reports/ReportGenerator.cs`
and `PlantUml/PlantUmlCreator.cs`. The measurements were taken with the published 5.2.1, whose drawing of notes 5.2.2
did not change. The harness is `LONG_NOTE_SCROLL_PLAN.harness/` (Appendix A): the code map every line here was taken
from (`research.md`), the measurements with the commands that took them (`measurements.md`, cited as M1 to M10), and
the generators and probes.

## 0. In short

- **The ask.** A payload note past a certain length gets a scrollbar instead of being split across several diagrams,
  and it stays fast on large diagrams.
- **Why it splits today.** Two rules in the page cut a diagram: an expanded note over 15,000 characters is cut into
  pieces, each in its own diagram with "..Continued On Next Diagram.." (`chunkLargeNotes`), and a diagram estimated
  taller than 12,000 px (18 px per note line) is cut into parts. Behind both is the drawing cost: under
  `skinparam wrapWidth` the engine paints every word and every space as its own `<text>`, so one 1,500-line response is
  11,001 elements, three diagrams and 26,894 px (M1a).
- **What changes.** A payload whose body would draw more than N lines (N = a new option, `PayloadScrollLines`) is drawn
  N lines tall, followed by a `...` line, and a scroll box inside the SVG (a `<foreignObject>` holding a `<div>` with
  `overflow: auto`) lies over its body and holds the whole body as text. The headers stay drawn above it. The box gets
  its text only when the note comes within a screen of the window; until then the note reads like a truncated one.
  Pictures copied from the menu draw the engine's rows (N lines and `...`), never the box.
- **What it costs, measured.** A test of 40 calls, four with a 1,500-line response and twelve with 150 lines, every
  payload expanded: 17 parts, 53,854 `<text>` elements and 2.54 to 2.59 s to draw today; 3 parts, 6,218 elements and
  1.18 to 1.19 s capped at 40 lines (M1b, 54% less time). A box costs the layout of its text once; filled only when near
  the window, 16 to 400 boxes cost nothing measurable before the first paint in Chromium, Firefox and WebKit (M4), and
  scrolling holds the frame rate. In BreakfastProvider's report 70 of 1,717 payloads are longer than 40 body lines, and
  with every note expanded the estimate is 15 diagrams split today against 4 capped (M9).
- **What the box needs from the page.** It stays in place at every zoom, scrolls with the wheel and keeps line breaks
  when copied in all three engines (M2), but the page's drag-to-pan, its click-to-select, its three arrow-key handlers,
  its hover buttons, its role pass, its picture copies and its "current" copies each need to know about it, and so do
  the redraw paths, which skip a diagram whose drawn source has not changed (section 4.4).
- **Found on the way** (section 6): the toolbar's Payloads -> Truncated judges a note's length by its JSON lines while
  the note shows YAML, so a note longer in YAML is left whole (F1, reproduced, M10); four more defects read in the code
  (F2 to F4, F7) and three false sentences in docs and comments (F5, F6).
- **Version.** The new option is new public surface: a minor. Turning it on by default would change what an unchanged
  project's report does, which CLAUDE.md files under MAJOR (4.0.0's display defaults were the precedent). So the plan
  ships it off by default (`0`, with 40 the documented value to set) in a minor, and the default moves to 40 at the next
  major; Q1 puts the choice to the owner.
- **Releases.** R0, a patch with section 6's fixes; then R1, the minor (section 8).

## 1. The ask

The owner, on 2026-10-21:

> Can we investigate the potential for when a note is a certain length, have a scrollbar on it, rather than having it
> awkwardly split between several diagrams. Is there any way of doing it in a performant way with large diagrams?

and, after the investigation (a prototype measured in the browser):

> Ok create a detailed well researched plan to implement it

Two requirements follow, and every decision below is held to them:

1. **No split for a long note.** A long payload is shown in one place, the whole of it reachable, with the diagram
   around it intact; the reader scrolls the note, not a chain of diagrams.
2. **Fast on large diagrams.** The change must cost less than today's drawing on the largest diagrams real reports have,
   and nothing where no note is long.

## 2. What happens today

### 2.1 The note states

A payload (a request, response or message body, drawn as a `note left` or `note right` block) has three states
(CN:319-324): collapsed (a one-line preview), truncated (the first N lines and a `...` line, CN:1951-1952) and
expanded. The report starts Truncated at 10 lines (RES:17-18), so a long payload shows ten lines until a reader expands
it, with its + or bottom ▼ button, a double-click or the toolbar's Payloads -> Expanded. Every change draws the diagram
again from a source composed for the new state (`composeNoteSource`, CN:1807-1813: formats, then width, then states,
then filters).

### 2.2 Why an expanded long note splits

1. **The 15,000-character rule.** `chunkLargeNotes` (RS:957-1024, `_maxNoteChars = 15000` at RS:669) cuts every
   `note left/right` block longer than 15,000 characters at line breaks. Each piece after the first opens with
   "..Continued From Previous Diagram..", each but the last ends with "..Continued On Next Diagram.." (RS:993-994), and a
   forced cut `== __SPLIT_BOUNDARY__ ==` (RS:1006) puts each piece in a diagram of its own.
2. **The 12,000 px rule.** `splitDiagramSource` (RS:865-935) cuts a diagram whose estimated height passes
   `BrowserFragmentMaxHeight` (12,000 px, `Constants/TrackingDefaults.cs:65`; 45 px per arrow, 18 px per note line,
   RS:670-671); the default is the fastest height measured on the current engine (`ReportConfigurationOptions.cs:162-169`).
   A call and its notes are never cut, so a long note ends a part.
3. **The drawing cost behind both.** Every generated sequence diagram carries `skinparam wrapWidth` (PC:1217), under
   which the engine paints each word and each space as its own `<text>` (found while fixing 4.26.6's copy defect, and
   re-measured in M1a). The page then finds its notes with `getBBox` over those elements (`findNoteGroups`, CN:167-271).

The other renderers (Server, Local, NodeJs) and the CI summary never run the page's scripts: the generator cuts their
response notes over 15,000 characters into separate diagrams (PC:1135-1195, only when the page does not split,
PC:1149). They keep doing so (section 4.7).

### 2.3 What it costs (M1)

| Report, every payload expanded unless stated | Parts | Height | `<text>` | Drawn in |
|---|---|---|---|---|
| One call, one 1,500-line response (YAML view, headers hidden) | 3 | 26,894 px | 11,001 | 1.4 s |
| 40 calls: 4 x 1,500, 12 x 150, 24 x 12 lines (YAML view, headers hidden) | 17 | about 144,000 px | 58,446 | 3.9 to 5.7 s |
| The same 40 calls, Truncated at 10 (the default start) | 2 | 8,440 px | 2,916 | 1.5 s |
| The same 40 calls (JSON view, headers shown) | 17 | about 129,000 px | 53,854 | 2.54 to 2.59 s |
| **The same, capped as section 4 designs it** | **3** | 19,180 px | **6,218** | **1.18 to 1.19 s** |

The last two rows are M1b: three steady runs each, after a first (cold) run of 6.75 and 2.64 s.

### 2.4 What real reports hold (M9)

BreakfastProvider's ReqNRoll lane (215 diagrams, 1,717 payloads, written by 4.26.4): half the payloads have 5 body lines
or fewer, 90% have 18 or fewer, 70 (4%, every one a response) have more than 40, and the longest is 3,973 lines and
91,991 characters; 16 are over 15,000 characters. With every note expanded, the splitter's arithmetic (an estimate,
M9) splits 15 of the 215 diagrams today (268 parts in all), and 4 (219 parts) with the notes capped at 40 lines.

## 3. What the platform gives (measured)

### 3.1 A scroll box inside the report's SVG

The prototype (`probe/proto2.js`, `probe/proto3.js`, `probe/kbd.js`) draws the 40-call report with its notes capped,
lays a `<foreignObject>` over each capped note's drawn body, puts a `<div style="overflow:auto">` holding the whole body
inside it and hides the engine's body rows. In Chromium 147, Firefox 148 and WebKit 26.4 (Safari's engine):

| Behaviour | Result | Measure |
|---|---|---|
| Zoom (the page sets the SVG's CSS width) | the box keeps its exact place and size in the note at 0.5x, 1x and 2x, in all three | M2 |
| Wheel over the box | the box scrolls, the page does not, in all three | M2 |
| Wheel at the box's end | Chromium and WebKit pass the next gesture to the page; Firefox holds it for the rest of the gesture | M2 |
| `overscroll-behavior: contain` | stops the hand-over in Chromium and Firefox; WebKit ignores it here | M2 |
| Ctrl+wheel over the box | zooms the diagram (the page's own handler), the box does not scroll | M3 |
| Drag inside the box of a zoomed diagram | **pans the diagram and selects nothing**, in all three | M3 |
| A selection over three lines, `toString()` | keeps both line breaks, in all three | M2 |
| `window.find()` for text deep in a box | found in all three; only WebKit scrolls the box to it | M2 |
| Keyboard focus | Chromium and Firefox focus a scroller on their own; WebKit needs `tabindex="0"` | M2 |
| Arrow keys on a focused box | **move between scenarios** (KN:16-31); kept from that handler, ArrowUp/Down, PageUp/Down, Home and End scroll the box in all three | M2, M8 |
| Pointer over the box | **the note's buttons do not show** (they listen on the note's paths, texts and a transparent rect over it, all under the box) | M2 |
| Right-click on the box | the menu finds the note (by position) and offers "Copy box text" | M2 |
| PNG copy of an SVG holding a box | exports in all three, the box's text painted | M6 |

### 3.2 The box's own cost (M4)

A box costs the layout of its text once, the first time it is laid out; scrolling it is the browser's work. Made
eagerly, 100 boxes of 1,500 lines cost 546, 337 and 449 ms (Chromium, Firefox, WebKit), and one box of 200,000 lines
676, 505 and 822 ms. `content-visibility: auto` removes the cost of boxes off the screen in Chromium and WebKit but not
in Firefox (730 ms for the 100 boxes). What works in all three is to give a box its text only when it comes within a
window height of the window (an `IntersectionObserver`): with 16 to 400 boxes, four were filled and the first paint
took only the frames the probe waited (61 to 67 ms in Chromium, 92 to 124 ms in Firefox at its headless 30 Hz, 94 to
108 ms in WebKit). A box still pays for its own text when it is filled: 3.3 to 3.5 microseconds a line in Chromium, 2.5
to 3.0 in Firefox and 3.8 to 4.4 in WebKit, so the largest real payload measured (3,973 lines) costs 10 to 18 ms when it
first comes near. (Under `content-visibility: auto` a line costs more, up to 9.8 microseconds in WebKit.)
Scrolling the boxes on the screen every frame held each engine's frame rate (Chromium 16.7 ms, WebKit about 15.7 ms with
occasional 30 to 46 ms frames, Firefox its headless 30 Hz).

### 3.3 Keeping the capped note as wide as the whole note (M5)

The engine sizes a note by the lines it is given, so a note cut to its first lines can come out narrower than its
widest line. On the current engine a line coloured `<color:#FFFFFF00>` keeps the note as wide as that line (793 px
against 62 without it), takes its row and paints nothing; a class with `MinimumWidth` on the note's header also sets a
width, but needs a number of pixels only the engine can measure. The plan uses the transparent line.

### 3.4 Scroll latching (M7)

A reader who scrolls the page with the wheel should not be caught by every box that passes under the pointer. With
synthetic wheel events (no gesture phases), Firefox kept the whole gesture on the page; Chromium and WebKit gave the
remaining notches to the box once it was under the pointer. Real devices send phased or timed gestures, which browsers
latch to where they began; that cannot be produced headless, so it is a manual check (section 7.5), with a fallback in Q6.

### 3.5 Alternatives, and why not

1. **Raise the 15,000-character limit.** The parts are a symptom: a 1,500-line note drawn in full is still 27,000 px of
   11,000 elements, and the 12,000 px rule then cuts the diagram after it.
2. **Scroll inside the SVG** (a `clipPath` and a translated group, with a scrollbar drawn in SVG). It rebuilds what the
   browser gives for free (wheel, drag, keys, touch, inertia, scrollbars, focus), keeps the text as per-word SVG
   `<text>` (the class of 4.26.6's copy defect), and needs the full drawing it is meant to avoid.
3. **An HTML box outside the SVG**, laid over it by position. It keeps copied pictures pure, but has to follow every
   zoom, resize and part and scale its text by hand; inside the SVG all of that comes free (M2).
4. **Draw only the lines in view** (a virtual list). Not needed below about 50,000 lines (0.13 to 0.19 s, M4); kept as the answer
   if a real payload outgrows that (Q7).

## 4. Design

### 4.1 Rules

1. **Which notes.** Payload notes: the `note left` and `note right` blocks the page already manages (`parseNoteBlocks`,
   CN:77-95), so requests, responses and message notes (`note<<eventNote>>`). Never `note over`, step bars or assertion
   notes, which the page does not fold either.
2. **What is counted.** A note's body: the lines the note would draw in its current state and format, after its meta
   lines (the `<b>Key</b>` line, the grey header lines and the blank line after them: the prefix `isNoteMetaLine` and
   `activeNoteContentLines` already recognise, CN:16, 1634-1647). Headers are not counted and never go in the box: they
   stay drawn above it, where a reader looks for the content type.
3. **When.** When the body would draw more than N lines, N being `PayloadScrollLines` (section 4.8; 0 means never). That
   is an expanded note longer than N, and a truncated note whose line limit is above N: Payload length offers 50, 60, 80
   and 100, and such a note draws N lines and its box holds the limit's lines and the `...`. So Truncated changes too,
   for limits above N. Collapsed notes draw one line and are never boxed.
4. **What the box holds.** Exactly the body the note would have drawn without the cap: the width breaks Kronikol marked
   joined again (`rejoinWrappedNoteLines`, CN:53-69) and the creole escaping undone as "Copy box text" undoes it
   (CM:367-378, 625-642): `decodeNoteEscapes` on a JSON body (CN:38-46; the menu's copy is CM:357-365) and
   `unescapeNoteDisplayLine` on the YAML view's lines (CN:1478-1485). The generator's own inline markup is drawn, not
   shown: a payload's `<` is always escaped (`~<` or `<U+003C>`), so a tag in a note line is Kronikol's, and the
   FocusFields emphasis (`<b>`, `<color:blue>`, `<size:9>`, `<color:lightgray>`, `PlantUml/JsonFocusFormatter.cs:242-261`,
   on a log whose `FocusFields` is set, `Tracking/RequestResponseLog.cs:50`) becomes bold, coloured or smaller text in
   the box, built as elements, never as markup strings.
5. **What is drawn.** The engine draws the note's meta lines in full, then N body lines, then a `...` line, then one
   transparent row: `<color:#FFFFFF00>` with the widest body line (its tags removed; the widest by measured width, a
   canvas `measureText` in the note font, not by characters) and about 3 em of no-break spaces, room for a classic
   scrollbar. The row makes the note as wide as the whole body would (M5) and is the row the expanded note's bottom
   button sits on. When no meta line is drawn (headers hidden, or none), a transparent spacer row goes first, the row
   the top-right buttons sit on. The box covers the N body lines and the `...` line.
6. **Everything else stands.** The note keeps its states, buttons, double-click, format and width controls. The
   15,000-character and 12,000 px rules stay for what they still cut: notes when the option is 0, and a diagram tall
   from its calls.

### 4.2 The drawn source, and the records that go with a picture

**Where the cap runs.** A new step, `capLongNotes`, inside `composeNoteSource` (CN:1807-1813) after
`buildSourceWithNoteStates` and before `applyFilters`: there every note still has its ORIGINAL index (filters drop notes
after it), its active format's lines and its state's lines (the `...` of a truncated note included). It finds the notes
with `parseNoteBlocks` (no new copy of the note-header pattern, which `NoteAppearanceScriptTests` counts), splits each
body from its meta lines with `isNoteMetaLine`, and rewrites the bodies longer than N to rule 5's shape.
`buildSourceWithNoteStates` and the `composeNoteSource(container, origSource, noteBlocks)` call are unchanged: their
signatures are pinned (`ScenarioTruncateAndCollapsibleNotesTests.cs:67`, `NoteFormatToggleScriptTests.cs:39`).

**Records.** For each capped note, `capLongNotes` records `{ text, lines }` under the note's original index (rule 4's
text), and keeps the source before the cap. `composeNoteSource` leaves them on the container in a pending slot
(`container._noteScrollPending = { records, uncapped }`), and every caller takes them at once, with the source they go
with: `_preProcessSource` (CN:2635-2695), `rerenderWithNoteStates` (CN:2036-2247), `processRenderQueue`'s `processNext`
(CN:2882-2884) and `startAhead`'s prefetch (CN:2846-2861).

**A picture and its records travel together.** The records describe one drawn source, and the cap makes different note
states draw identical sources (Expanded and Truncated at 100 both draw 40 lines, `...` and the same transparent row
when the widest line is within the first 100). Today's redraws skip a diagram whose drawn source has not changed
(`drawnFrom`, CN:1999-2001, used at CN:2886), and a failed redraw restores the old state and decorates the old picture
(CN:2212-2222, 2234-2245). So:

1. The records become the picture's: they are installed on the container (`container._noteScrollDrawn`) at the moment
   its picture is swapped in, in one step with `recordDrawn` (CN:1995-1998), and on the first drawing before
   `_makeNotesCollapsible` (RS:1376). The bulk queue's swaps call `recordDrawn` today (CN:2915-2934, 2999-3034); a note's
   own redraws do not (CN:2077-2085, 2164-2201), and a picture drawn there fails `drawnFrom` only because it is another
   `<svg>` element (CN:2000). R1 makes all four swaps call the one step. A failed redraw keeps the old picture's
   records.
2. `recordDrawn` also records the source before the cap, and `drawnFrom` compares both. When only the uncapped source
   differs (the picture is right, the boxes and buttons are not), the diagram is decorated again with the new records
   and no engine call.
3. `makeNotesCollapsible` reads `_noteScrollDrawn` only.

**"Current" source.** The context menu's "Copy current PlantUML source" and "Open current PlantUML in new tab"
(CM:756-769, 818-834) read the uncapped source kept with the picture, so a copied source never holds the transparent
rows (Q4); and the menu's choice of a full/current submenu (CM:757, 820) compares the original with it.

The capped source is short, so the splitter's estimate (RS:839-862) and the 15,000-character rule see the capped note
as the engine will draw it: nothing in the splitter changes.

### 4.3 The box

**When.** `makeNotesCollapsible` (CN:816-1073), after setting each drawn note's `_noteIndices` (CN:991), registers each
drawn note whose original index has a record with one shared `IntersectionObserver` (`rootMargin: '100% 0px'`) on the
note's body path. When the note comes within a window height of the window, the box is made and filled. Until then the
note shows its drawn rows: N lines and `...`. (`content-visibility: auto` was measured as the alternative and does not
spare Firefox, M4.)

**Element.** `<foreignObject class="note-scroll-frame">` holding `<div class="note-scroll" tabindex="0"
aria-label="Payload, 1,500 lines">`, inserted right after the note's last `<text>` inside the SVG's first `<g>`: above the
note's fill, below the buttons the page appends to the SVG root (CN:763), outside the run of paths `findNoteGroups` reads
(CN:181-189), and skipped by its scan of non-text elements inside the note (CN:210-223).

**Place.** Left at the body text's `x`, right 4 px inside the note's body path, top at the first body row's top, bottom
at the `...` row's bottom (the transparent row paints no `<text>`, so it cannot be read; it lies below). Read from the
drawn rows the box hides: their `y` and `getBBox` stay valid under `visibility: hidden` (the page already reads the
hidden fold, CN:132-165, IS:92).

**Look.** Font family and size read from a hidden body `<text>` (`system-ui` where the page draws the diagram,
`PlantUml/DiagramLook.cs:13` written at PC:1215; 11 px on a message note, PC:1276-1282), line height from the distance between two body rows, colour `var(--dg-ink)` over the note's own fill.
`white-space: pre-wrap; overflow-wrap: anywhere; overflow: auto`, `overscroll-behavior` left at `auto` (section 4.5), and
a visible focus ring. The generator's colours (rule 4) go through the mapping the page applies to the engine's ink
where one exists (IS:108, 134), else as written. The rules go in `collapsible-notes-styles.css`, with no literal colour
(`ReportSheetColoursTests`).

**The rows under it.** The hidden body rows and the `...` row take the class `note-scroll-under` (`visibility: hidden`)
when the box is filled, not before.

**Again and again.** `makeNotesCollapsible` runs more than once on one SVG (after a failure, a width refine, a deferred
decoration): it removes `.note-scroll-frame`, the class and the observer's registrations with its other leftovers
(CN:820-822). The SVG cache holds the engine's markup before decoration (CN:2119, 2193), so a restored picture is boxed
again.

**Scroll position.** A passive `scroll` listener keeps `owner._noteScrollTop[originalIndex]`; a new box for that note (the
diagram drawn again by a toggle, a format change, a redraw) starts there once filled (Q9).

### 4.4 The page's other code

| Where | Today | Change | Why |
|---|---|---|---|
| Redraws (`drawnFrom`, `recordDrawn`, the swaps) | skip a diagram whose drawn source is unchanged | section 4.2: records installed with the picture; a changed uncapped source decorates again | a skipped or failed redraw left another state's box and buttons (review) |
| Hover buttons, `createNoteButtons` (CN:701-719) | listen on the note's paths, its texts and a transparent rect over the whole note (CN:740-760), all under the box | the frame gets the same `mouseenter` / `mouseleave` pair | the buttons never showed over the box (M2) |
| The tall-note button cluster (CN:677-691) | follows the visible top of a note taller than 500 px | stays in the header (or spacer) row of a boxed note | it floated over the box's scrollbar |
| Hidden buttons (CN:694-698) | opacity 0, still clickable | `pointer-events: none` while hidden, for every note | an invisible "Fold" or "Cut" over a box took the click |
| Width button, `canWiden` (CN:1019-1026) | painted rows against the drawn source's lines | for a boxed note, whether the box's text wraps (its laid-out height against its lines) | the transparent rows count as source lines and paint nothing (M5) |
| Double-click (CN:705-707) | cycles the note's state on its paths | nothing on the box: it selects a word, as on the note's text | |
| Click to select a diagram (CM:930-940) | any click in a diagram (or a part, `zoomBoxOf`, CM:25-30) toggles its selection | a click inside `.note-scroll` leaves it | placing the caret or clicking the scrollbar flipped the selection |
| Drag-to-pan (CM:1181-1218) | a mousedown in a zoomed diagram or part pans and `preventDefault`s | a mousedown inside `.note-scroll` does not start a pan | a drag in the box panned and selected nothing (M3) |
| Ctrl+wheel (CM:1163-1177) | zooms | nothing | already zooms over the box (M3) |
| Arrow keys (KN:1-5; `report-tabs-script.js:71-72`; `report-features-tools-script.js:204-209`) | KN moves between scenarios; the two others, in the capture phase, go to the Features tab and its Full view | each returns for a target inside `.note-scroll` (one shared test) | a box's own keydown handler cannot stop the capture-phase two (M8) |
| Role pass (RS:1893-1897) | excludes the two note decorations from what it measures | excludes `.note-scroll-frame` | it reads every element of the main `<g>` |
| Picture copies, `lookClone` (CM:92-129; the SVG and PNG copies both call it, CM:135, 199) | clones the SVG, then inlines computed styles pairwise (CM:94-120) | after that loop, drops every `.note-scroll-frame` and shows the `note-scroll-under` rows | a copied picture is plain SVG that reads like a truncated note (Q5) |
| "Copy/Open current box text" (CM:655-678, 683, 836-844) | built from the drawn source | for a boxed note, the record's text | the drawn source holds the transparent rows (`NOTE_HEADER_TAG`, CM:7, strips only six-digit colours) |
| Copy Highlighted Text (CM:696-729) | `selectedSvgText` returns null for HTML text, then `Selection.toString()`, mapped onto the note's text (onto `_currentNoteText` when not expanded, CM:702-704) | the mapping uses the record's text for a boxed note | a selection in the box keeps its line breaks (M2) |
| "Current" source items (CM:756-769, 818-834) | the drawn source | the uncapped source kept with the picture (4.2) | |

Read and left as they are: the note hit test (it finds the box's note by position, M2), "Copy box text" and "Open box
text in new tab" (they read the original source), the link binder (it walks SVG `<text>` and a box holds none), the
search (it reads sources), Export Filtered HTML (it empties every drawn diagram), `findNoteGroups` and the empty-note
index map (a `<color:#FFFFFF00>` row is not a header line), the format eligibility check, `isLongNote` and
`collapsedIsWhole` (they read the original source). Research section 2 lists each with its lines.

### 4.5 Wheel, keys and focus

The box takes the wheel as any scrolling element does: the wheel scrolls it and, at its end, the next gesture goes to the
page (`overscroll-behavior: auto`: `contain` would leave a reader stuck at the end in Chromium and Firefox, and WebKit
ignores it, M2). A gesture that began on the page should stay there; that is the browser's latching, not measurable
headless (M7), so it is a manual check (section 7.5) with a fallback held in Q6.

The box is a Tab stop (`tabindex="0"`, which WebKit needs, M2, and axe's `scrollable-region-focusable` rule asks of a
scrolling region), named by its `aria-label`; with the three handlers' exemption, ArrowUp/Down, PageUp/Down, Home and End
scroll it (M8). This departs from the accessibility note above `processRenderQueue` (CN:2708-2720), which asks for one
Tab stop per diagram and decoration on focus: a box exists only once its note comes near the window, so the Tab order
gains boxes as the reader scrolls. The note's model is not built yet; Q12.

The browser's find bar searches a filled box (M2: found in all three engines; whether the bar scrolls the box to the
match is a manual check), not a box that is not filled yet, nor a diagram not yet decorated, which shows N lines.

### 4.6 Performance

1. **Less to draw.** A capped note sends the engine N + 3 body lines instead of its whole body (M1b: 53,854 to 6,218
   elements, 17 parts to 3, 2.5 s to 1.2 s). Toggles that draw every diagram again (5.1.1) draw smaller sources too.
2. **Boxes only near the window.** A box is made and filled when its note comes within a window height of the window
   (4.3). M4: 16 to 400 boxes on a page, four filled, no measurable cost before the first paint in any engine.
3. **Diagrams far away wait as before.** Decoration of diagrams far from the screen waits until they come near
   (`afterRedraw` and `nearWatch`, CN:2747-2796, 5.1.1); the boxes are made after it.
4. **No script runs per frame.** Scrolling is the browser's; the only listener is a passive `scroll` that stores a number.
5. **One cost left.** A box coming near the window lays out its whole text once: 10 to 18 ms for the largest real
   payload measured (3,973 lines), 0.13 to 0.19 s at 50,000 lines and 0.5 to 0.8 s at 200,000, across the three engines
   (M4). Q7.

### 4.7 What does not change

- The other renderers (Server, Local, NodeJs) and the CI summary keep cutting long responses into diagrams
  (PC:1135-1195); none runs the page's scripts (RG:1636).
- Collapsed notes, Truncated at limits up to N, the line limit's control, the headers, the JSON and YAML views and the
  note width. (Truncated at a limit above N changes, rule 3.)
- `BrowserFragmentMaxHeight` and the 15,000-character rule, which still cut what is not capped.
- Kronikol4J, whose page is 3.0.43's (a ledger entry, section 8.5).
- A printed page shows each box as it stands, scrolled where the reader left it (the page has no print rules today). Q8.

### 4.8 The option

`ReportConfigurationOptions.PayloadScrollLines` (int): the most body lines a payload draws before it scrolls; 0 never
caps; otherwise 10 to 1,000. Default 0 in R1 and 40 at the next major (Q1); the docs name 40 as the value to set. Under
BrowserJs only; the Server, Local and NodeJs renderers keep today's split.

It reaches the page the way `NotePayloadFormat` does (`ReportConfigurationOptions.cs:593-600` feeds the resolved
defaults' built-in, RES:61-64): a new member of `ResolvedToggleDefaults` (RES:9-47), which `Overlay` (RES:71-100) must
carry like every member (it builds `new() { ... }`; `NotePayloadFormat` survives only because it is listed at RES:81),
seeded into the notes script by `GetCollapsibleNotesScript` (`Reports/DiagramContextMenu.cs:146-162`) as
`window._payloadScrollLines`. The range is checked where the value is used, in `GetCollapsibleNotesScript`: a caller
can hand `GenerateHtmlReport` a `ResolvedToggleDefaults` of its own (RG:1328, 1461) and so pass the resolver by. That
path reaches both reports (RG:531, 536) and `kronikol merge` (`Reports/Merge/MergeableReportRenderer.cs:119`) without
touching a public method's signature (an optional parameter added to one is a binary break). `kronikol merge` renders
with default options (`src/Kronikol.Tool/MergeCommand.cs:138`), so a merged report uses the default, as it does for every
option; `kronikol ingest` gets no flag unless the owner asks (Q3). No toolbar control in R1 (Q2): the report's author
sets it, and a reader who wants a payload whole has "Open box text in new tab".

## 5. Code

### 5.1 C#

| File | Change |
|---|---|
| `ReportConfigurationOptions.cs` | `PayloadScrollLines` and its doc: what it caps, 0, the range, BrowserJs only, what the other renderers do. F5 (:609). |
| `Constants/TrackingDefaults.cs` | `PayloadScrollLines = 0` (40 at the next major), beside `BrowserFragmentMaxHeight` (:65). |
| `Reports/ReportToggleDefaultsResolver.cs` | `ResolvedToggleDefaults.PayloadScrollLines`, its built-in from the option (as at :61-64), carried by `Overlay` (:71-100). |
| `Reports/DiagramContextMenu.cs` | `.Replace("__PAYLOAD_SCROLL_LINES_DEFAULT__", ...)` beside :151, with the range check. |
| `Reports/ReportGenerator.cs` | the Expanded button's title, "The payloads in {diagrams} show in full" (:1091), gains "; a long one scrolls in its box" when the option is set. |

### 5.2 Page

| File | Change |
|---|---|
| `collapsible-notes-script.js` | `capLongNotes` and the pending slot in `composeNoteSource` (4.2); the callers take the slot; `recordDrawn`/`drawnFrom` and the swaps install the records and compare the uncapped source; `_preProcessSource` composes when a note would be capped (CN:2690-2694 skips the composer when every note is expanded, shown as JSON with headers shown and no width change); the box, its observer and its cleanup in `makeNotesCollapsible` (4.3); the hover pair, the cluster and the hidden buttons in `createNoteButtons`; `canWiden`; the global (CN:2278-2291); F1, F2, F3, F6 and F7. |
| `collapsible-notes-styles.css` | `.note-scroll`, `.note-scroll:focus-visible`, `.note-scroll-under`, hidden buttons without pointer events. |
| `context-menu-script.js` | the click and the pan skip `.note-scroll`; `lookClone` drops the frames and shows the rows; the "current" text and source items read the picture's records and uncapped source; F6's second sentence (CM:506-507). |
| `report-keyboard-navigation-function.js`, `report-tabs-script.js`, `report-features-tools-script.js` | return for a target inside `.note-scroll`. |
| `plantuml-browser-render-script.js` | the first drawing installs the records before decoration (RS:1376); the role pass excludes `.note-scroll-frame` (RS:1893-1897); F4. |

### 5.3 Constraints carried over

- `buildSourceWithNoteStates`'s five parameters and the `composeNoteSource(container, origSource, noteBlocks)` call are
  pinned (`ScenarioTruncateAndCollapsibleNotesTests.cs:67`, `NoteFormatToggleScriptTests.cs:39`): the cap is a step of
  its own, its records a side slot.
- `DiagramContextMenuTests.GetFunction` slices a function at the next top-level `function` or `window.` (:157-170): new
  helpers go where they do not cut a sliced function short.
- `NoteAppearanceScriptTests` counts four copies of the note-header pattern (:41) and requires its `\s*$` variant
  (:42): the cap reuses `parseNoteBlocks`.
- `NoteCopyFidelityScriptTests` counts seven `noteLinesToText(` (:35) and one copy of the painted-text expression (:47)
  in the context menu: the box's text is made in the notes script, with the functions those wrap.
- `StylesheetRulesTests` (one phone breakpoint) and `ReportSheetColoursTests` (no literal colour) hold the new rules.
- The box is part of the note's decoration and shares its closures, so it lives in `collapsible-notes-script.js` (a new
  script file would also need its own place in the page's script order, RG:1734-1736).

## 6. Bugs found on the way

CLAUDE.md asks for every defect found to be fixed, test first. F1 was reproduced; F2 to F4 and F7 were read in the code
and the lines re-read by hand; F5 and F6 are false sentences. Each fix starts with a fact that must fail on the code before it; a
defect whose fact cannot be made to fail is dropped, and section 10 says so.

| # | Defect | Where | Evidence | Fix |
|---|---|---|---|---|
| F1 | Payloads -> Truncated (the report's or a scenario's) decides which notes are long by their JSON lines, while the first drawing and the note's own buttons count the lines shown, YAML by default since 4.0.0. A body whose YAML is longer than its JSON (a string with line breaks becomes a block, a line each) and straddles the limit stays whole after Expanded then Truncated. | `buildDetailsQueue` (CN:3074) and `buildHeadersQueue` (CN:3103, 3115) call `isLongNote` on `noteBlocks[i].contentLines`; `makeNotesCollapsible` (CN:999) and `_preProcessSource` (CN:2683) use `activeNoteContentLines` (CN:1634-1647). | **Reproduced** (M10): the body is 18 lines in the YAML view; 11 rows with `...` at load, 18 after Expanded, still 18 after Truncated. | Both queues read `activeNoteContentLines(container, i, noteBlocks[i].contentLines)`. Three pins require the old first argument (`ScenarioTruncateAndCollapsibleNotesTests.cs:77, 172, 180`): they move to the new one and keep what they hold, the container's limit and the hiding state passed on. |
| F2 | A note's redraw on a diagram drawn whole waits 30 s when the engine answers with text (a failure) instead of an SVG. Meanwhile every note button on the page is ignored (`setNoteState`, `setNoteFormat` and `setNoteWidth` return while `window._plantumlRendering` is set, CN:2006, 2018, 1875) until a toolbar control's queue, which waits up to 15 s for that flag, clears it (CN:2865-2873); the failed diagram's own buttons stay held for the whole 30 s. | `rerenderWithNoteStates`, the branch for a diagram drawn whole: its observer waits for an `<svg>` (CN:2203-2208), `render` is called with no `onError` (CN:2211), so the engine's wrapper writes the failure as text, and the poll gives up after 120 x 250 ms (CN:2225-2246). The branch for parts and the bulk queue take text as an answer (CN:2130, 3035-3040). | Read; not reproduced (it needs an engine failure on that path). | Take text as the end of the drawing, as the other two paths do, and restore the note's state as the branch's `catch` does (CN:2212-2222). The fact makes the page's `window.plantuml.render` write a failure for one call (a stub in the page, not a network mock). |
| F3 | After Wide on a note of a diagram drawn in parts, the one correction pass that fits the widened note to the column never runs; nor does it on such a diagram's first drawing when the report starts with Payload width Wide (`NoteWidth = Full`). | `setNoteWidth` is called with the owner (CN:1029) and sets the flag on it (CN:1884), as `_preProcessSource` does for a report that starts Wide (CN:2665-2669); the pass in `makeNotesCollapsible` reads the flag on the element it decorates (CN:1061), and every path that draws parts decorates each part: a note's redraw (CN:2090), the bulk queue (`afterRedraw` checks the owner's flag, then `decorateNow(el)` decorates the part, CN:2765, 2776-2783) and the first drawing (RS:1124-1139, then RS:1376). | Read. | Run the whole pass against the owner, which `makeNotesCollapsible` already finds (CN:825): its flag, its `_noteWidthPx` (CN:1880) and its redraw (`rerenderWithNoteStates` reads the owner's `_noteOriginalSource`, CN:2037-2038), with the widest part as the drawn width. |
| F4 | When the engine reports "too large" and the page retries at half the height, the smaller parts are queued in reverse: the bottom part draws first and the top one, usually the one on the screen, last. | `renderQueue.unshift` inside a forward loop (RS:1414-1421). | Read. | Queue them in order. The fact records the order in which the parts receive their SVG. |
| F5 | The doc of `DiagramNoteWrapWidth` says the reader's "per-note full-width and monospace toggles are client-side"; the monospace control was removed in 4.0.0 (`5ab2a3ea`). | `ReportConfigurationOptions.cs:609` | Read. | Drop "and monospace". |
| F6 | Two comments say the diagram's context menu holds every note action; its note items copy and open text only (no expand, fold, format or width). | CN:2717-2718 (in the accessibility note, CN:2708-2720) and CM:506-507 | Read, and the menu's items seen in M2. | Say what the menu holds. |
| F7 | A note's redraw on a diagram drawn whole keeps an engine failure that comes back as a picture (the engine's stack-overflow drawing, which `describeEngineFailure` recognises, RS:1290) as if it were the diagram: it is not described, and it is cached under its source, so the same source later draws the failure from the cache. | The branch's `afterRender` swaps and caches with no check (CN:2187-2201, the cache at 2193); every other path describes the result and caches only a success (CN:2118-2119, 2965-2966, 3024-3027, and the first drawing at RS:1368). | Read (found by the plan's second citation check). | Describe and cache as the other paths do; one change with F2, in the same branch. The fact has the stubbed render answer with the stack-overflow picture: it is described, and drawing the source again calls the engine. |

Noticed and left for the owner (Q11): the page's own SVG cache (`_svgCache`, CN:1989) is never trimmed, while the
engine's cache is bounded by `BrowserRenderCacheMegabytes`.

## 7. Tests

Fixtures draw like real reports: the generator writes the source (its prefix carries `skinparam wrapWidth`,
`system-ui`, the `<style>` block and the teoz pragma), never a hand-written stand-in; facts about painted text on a
source without `wrapWidth` passed on the copy defect 4.26.6 fixed. The fixtures pin the JSON view and headers shown, as
`ReportTestHelper.ClassicStart` does (RTH:25-30), so a note's drawn body is its source body and "40 lines" means 40
drawn lines. They set `PayloadScrollLines` through a helper that sets the member by reflection when it exists, so every
fact also compiles against the code before the change and its red there is an assertion, not a build error.

### 7.1 Playwright: `LongPayloadScrollTests` (new; Notes collection)

A new `ReportTestHelper` builder writes one scenario through the generator from captured calls whose JSON responses have
12, 40, 41, 150 and 1,500 body lines, one message note of 60 lines, and one response drawn with FocusFields emphasis (a
log with `FocusFields` set, `Tracking/RequestResponseLog.cs:50`); a second scenario has 300 calls with a 60-line response
each (drawn in parts by height). The option is 40 unless a fact says otherwise. Each fact is run red on the code before
the change (5.2.2 at this writing), except the three controls.

| # | Fact | Red before the change because |
|---|---|---|
| T1 | An expanded 1,500-line response is drawn in one part, with no "Continued" text; its box holds every body line, and the note is as wide as the same note uncapped (within 2 px) | the note is three diagrams |
| T2 | The 40-line response has no box; the 41-line one has one, drawn 40 lines tall | no box exists |
| T3 | The box's text equals what "Copy box text" copies for that note (the menu's own reading of the captured body: width breaks joined, creole escapes undone) | no box |
| T4 | Headers stay drawn above the box; with Headers hidden the box starts below the row of the top-right buttons | no box |
| T5 | The wheel over the box scrolls the box, not the page | no box |
| T6 | Zooming with the slider keeps the box's place in the note (fractions equal within 0.001) | no box |
| T7 | Hovering the box shows the note's buttons | no box |
| T8 | A drag inside the box of a zoomed diagram selects text and leaves the diagram where it was | no box |
| T9 | A click inside the box leaves the diagram's selection as it was | no box |
| T10 | The box is a Tab stop named "Payload, 1,500 lines"; focused, ArrowDown, ArrowUp, PageDown, PageUp, End and Home scroll it, and no scenario, tab or view changes | no box |
| T11 | Copy Highlighted Text over three lines of the box copies those lines, line breaks included | no box |
| T12 | Copy as SVG of the diagram holds no `foreignObject`, draws the 40 body lines and `...`, and hides no body row; Copy as PNG decodes | no box |
| T13 | "Copy current PlantUML source" holds the whole body and no `<color:#FFFFFF00>` row | (a control: passes before the change, where nothing is capped; fails on R1 if the item reads the drawn source) |
| T14 | JSON -> YAML on a boxed note boxes the YAML body; Truncated with Payload length 100 boxes the first 100 lines and `...` | no box |
| T15 | A report that starts Expanded (`Details = Expanded`) caps on its first drawing | the first drawing skips the composer |
| T16 | `PayloadScrollLines = 0` draws as 5.2.2 does: the 1,500-line note in three parts | (a control: passes on both) |
| T17 | The 300-call diagram still draws in parts, and every long note in every part gets its box as it comes near | no box |
| T18 | A box keeps its scroll position across a Headers toggle | no box |
| T19 | Dark theme: the box's text colour equals the drawn ink, and follows a theme switch made with the box filled | no box |
| T20 | Databases hidden (a note before the boxed one dropped): the box holds its own note's body | no box (and it pins the original index) |
| T21 | Performance: the 40-call report of M1b, every payload expanded, draws in at most 3 parts under a budget scaled by `ContentionScale`; 16 notes carry a record and only the boxes near the window are filled | 17 parts |
| T22 | Payload length 100, then Expanded, on a note whose widest line is in its first 100: the drawn source is the same, so the diagram is not drawn again, and the box then holds the whole body, not 100 lines | no box |
| T23 | A note redraw that the engine fails (the F2 stub) leaves the old picture, its box and the box's text | no box |
| T24 | On a boxed note taller than 500 px, the top-right buttons stay in its header row when the page scrolls past the note's top, and a hidden button takes no click (a click there reaches the box) | no box |
| T25 | The FocusFields response: the emphasised field is bold and coloured in the box, no `<b>` or `<color` text shows, and the note is as wide as its widest line drawn | no box |
| T26 | Truncated at 100: "Copy current box text" copies the 100 lines and `...` | (a control: passes before the change; fails on R1 if the item reads the drawn source) |
| T27 | After Wide and its correction pass, each boxed note has exactly one box | no box |

Plus, for section 6: F1 (the M10 report: Expanded then Truncated cuts the YAML note to 11 rows with `...`), F2 (the
stubbed failure on a whole-diagram note redraw frees every note button within 2 s), F3 (Wide on a note in a diagram drawn
in parts brings the drawn width within 24 px of the column, and so does a report that starts Wide), F4 (the retry's parts
get their SVG top first), F7 (the stubbed stack-overflow picture on a whole-diagram note redraw is described, and the
same source then draws through the engine, not the cache).

### 7.2 Core (`tests/Kronikol.Tests`)

- `ReportToggleDefaultsResolverTests`: the built-in is 0; the option flows into the resolved record; `Overlay` carries
  it (a record overlaid with the member set reads it back).
- The range, where the value is used: `GetCollapsibleNotesScript` with 9 or 1,001 throws with the range in the message,
  both from the resolver's path and from a `ResolvedToggleDefaults` built by hand and passed to `GenerateHtmlReport`;
  0, 10 and 1,000 pass.
- `ToggleDefaultsMarkupTests`: `__PAYLOAD_SCROLL_LINES_DEFAULT__` joins the token list (:81-86), and the built-in
  global reads `window._payloadScrollLines = 0` (the fact at :88). `ToggleDefaultsBaselineTests` holds unset = built-in
  on its own.
- The Expanded title: with the option set it gains "; a long one scrolls in its box"; unset it is today's, so its pins
  hold (`ControlTitlesTests.cs:314`, `ScenarioToolbarTests.cs:148`, `ToggleDefaultsMarkupTests.cs:178, 184, 622`).
- `DiagramContextMenuTests`: pins for the cap as a step of `composeNoteSource` after the states and before the filters,
  for the role pass's exclusion, and for the menu's exemptions (click, pan, `lookClone`, the current text and source
  items).
- The guards in 5.3 stay green unchanged.

### 7.3 Pins that move

With the default 0, R1 moves no pin: every existing fact draws as before, and T16 holds that. The flip to 40 (Q1) will
need the full Playwright suite (4.0.0's header and YAML flips turned 77 facts red across 20 classes). Research section 5
lists the facts that expand a note past 40 body lines, read its drawn rows or need the 15,000-character pieces; for the
flip they sort three ways:

1. **Written for the full drawing**: `ContinuationNoteInteractivityTests`, `ThreeFragmentContinuationTests`,
   `CapturedTextEscapeTests` :236 and :260, `NoteButtonLayoutTests` :217 and :239, `DiagramNoteSplitTests`,
   `DiagramNotePartitionTests`, `DiagramNoteLongNoteTests` and `NoteButtonsAfterHeaderHideTests`. Their fixtures pin
   `PayloadScrollLines = 0`, as `ClassicStart` pins 4.x's start states; adding it to `ClassicStart` is the one-line
   version, checked class by class, since a fact written for the default must not be pinned away from it.
2. **Named for the default**: the Expanded title's pins listed in 7.2, turned round to the new title.
3. **Reading a long note's text**: `DiagramNoteContentTests` :29, `YamlNoteCopyTextTests` :96, :114 and :138,
   `ToggleDefaultsTests` :49 and :66, `NoteYamlToggleTests` :366: they read the box when the note is boxed, and say so.

Each red is read before it is moved: a flip can expose a latent bug in the newly default path.

### 7.4 Proofs

- Every fact in 7.1 red before the change, with the failing assertion read (a fact can fail for the wrong reason); the
  controls T13, T16 and T26 green on both.
- Mutations, each to be caught by a named fact: the cap one line late (T2); no transparent row (T1's width); the widest
  line chosen by characters (T25); no spacer row with headers hidden (T4); box text from the composed lines without
  rejoining (T3); a record keyed by the drawn index (T20); records read from the compose instead of installed with the
  picture (T22, T23); `drawnFrom` not comparing the uncapped source (T22); no hover pair (T7); the cluster floating over
  a boxed note (T24); hidden buttons taking clicks (T24); no pan exemption (T8); no click exemption (T9); no key
  exemption, one mutation for each of the three handlers (T10); `lookClone` keeping the frame (T12); `lookClone` leaving
  the rows hidden (T12); the current source or text read from the drawn source (T13, T26); `_preProcessSource` not
  composing (T15); the cleanup missing the frame (T27); the scroll position not kept (T18); every box filled at once
  (T21's count).
- The acceptance (8.4) is a deliverable: run and recorded before the tag.

### 7.5 Browsers, load and what only a person can check

CI's Playwright lanes install Chromium only (`.github/workflows/ci.yml:242-246`). Before the release the harness's
probes run against the built page in Chromium, Firefox and WebKit (Appendix A), and section 10 records the table. By
hand, on real devices: a mouse wheel and a trackpad scrolling the page past boxes (does a gesture that began on the page
stay there? M7); the browsers' find bars on text deep in a filled box (M2's `window.find` scrolled the box only in
WebKit); touch scrolling on iOS Safari and Android Chrome at phone width; Safari on macOS; print preview; and the Tab
order with a screen reader (Q12).

## 8. Releases and where the work happens

### 8.1 Order

The plan is written against 5.2.2 (`4a0dad9e`), whose pan, click and zoom code (`zoomBoxOf`, `ownSlider`, a click
dropped after a drag) is what section 4.4 exempts the box from. Work under way on 2026-10-21 for 5.2.3 (a diagram keeps
its zoom when it is drawn again) changes the same picture swaps that 4.2 installs the records at; R1 starts from
whatever `origin/main` then holds, in a worktree of its own, and re-reads those paths first.

### 8.2 Steps

1. **R0, patch:** F1 to F7, each test first (F1 moves three pins, section 6). Full core and Playwright suites,
   `release.slnf` in Release, changelog, tag, wiki (the Payloads text where F1 changes behaviour), Kronikol4J ledger.
2. **R1, minor:** section 4. In order: the option and its plumbing (7.2); the cap with T1, T2, T15, T16; the records
   with T22, T23; the box with T3, T4, T6, T19, T20, T25, T27; the page's exemptions with T5, T7 to T11, T24; the copies
   with T12, T13, T26; then T14, T17, T18; T21 and the acceptance; the docs. A full Playwright run before the tag, though
   no pin moves.
3. Each release claims its number at push ("taking X now" to any live peer), per the parallel-session practice.

### 8.3 Version

R1 adds a public option and leaves every report as it was until the option is set: a minor (CLAUDE.md: a new
configuration option is a minor bump even when its default preserves today's behaviour exactly). Making 40 the default
would change what an unchanged project's report draws, which CLAUDE.md's MAJOR line names ("changing a default so that
existing code behaves differently without being touched"), as 4.0.0 did for the display defaults (`5ab2a3ea`); so the
flip waits for the next major (`plans/V6_PLAN.md`, where a line is added if Q1's (a) is taken). CLAUDE.md also says a
change to report output is not on its own a major; here the default is what would make it one.

### 8.4 Acceptance

On the built R1 with the option at 40: M1b and M2 again from the harness (`probe.js`, `proto2.js` and `kbd.js` reduced
to reading the product's own boxes) in Chromium, Firefox and WebKit, against 5.2.2 as the control; BreakfastProvider's
ReqNRoll lane run locally against the R1 build (nothing pushed to that repository), its parts counted with every note
expanded, against M9's estimate; and the manual checks of 7.5. The numbers go in section 10.

### 8.5 Documentation

- Wiki (at `b01d13a`):
  - `Large-Response-and-Diagram-Handling.md` :10 (the response note row: under BrowserJs with the option set, a long
    payload scrolls instead), :11, :22 (a note split across parts), and a new section on the scroll box;
  - `PlantUML-Browser-Rendering.md` :72 (the options list), :136-146 (the report's controls on a large report draw
    smaller sources with the option set), :148-157 (zoom on a diagram drawn in parts: inside a box the wheel scrolls and
    a drag selects), :218 and :279-283 (copies: a boxed note's text is its whole body);
  - `Generated-Reports.md` :778-791 (Payloads: with the option set, Expanded, and Truncated at a limit above it, scroll a
    long payload);
  - `Report-Configuration.md`: the option's row beside :138, and a row in the start states' Settings table (:245-275)
    only if Q2's (b) makes it a start state;
  - `Inline-SVG-Rendering.md` :101-114 (the SVG menu: Copy as SVG and as PNG draw the engine's rows, not the box; Copy
    Highlighted Text inside a box).

  Grep the wiki for "Continued From Previous Diagram", `BrowserFragmentMaxHeight` and "show in full" before the push.
- Repo: the option's XML doc; F5; the CHANGELOG, with what the option changes first; the README only if it describes
  note drawing.
- Kronikol4J ledger (`docs/REMAINING_PARITY.md` on the port's `origin/main`): "Not mirrored: the port's page is
  3.0.43's; its expanded notes are still cut at 15,000 characters". R0's F1, F4 and F7 entries say whether the port's copies
  share them.

## 9. Questions for the owner

Recommended answers first; the plan above is written to them.

1. **Q1. The default.** (a) Off in R1 (`0`, a minor), with 40 the documented value to set, and 40 by default at the next
   major (recommended: CLAUDE.md puts a default that changes an unchanged project's report under MAJOR; in
   BreakfastProvider 40 touches 70 of 1,717 payloads, every one a response of 41 to 3,973 lines, and leaves the rest as
   they are); (b) on at 40 now, which makes R1 a major and is the owner's call alone; (c) off for good.
2. **Q2. The control and the name.** (a) An option only, `PayloadScrollLines`, no toolbar control (recommended: the
   toolbar is crowded, and "Open box text in new tab" already shows a payload whole); (b) also a select beside Payload
   length ("Scroll after: 20, 40, 100 lines, never"), which makes it a start state in `ReportToggleDefaults` with
   presets; (c) another name.
3. **Q3. The CLI.** (a) No `kronikol ingest` flag (recommended until asked; merge uses defaults, as for every option);
   (b) `--payload-scroll <n>`, modelled on `--headers`.
4. **Q4. "Copy current PlantUML source".** (a) The source before the cap (recommended: the cap is how the page shows a
   payload, not part of the diagram); (b) the drawn source, transparent rows included.
5. **Q5. Copied pictures.** (a) Plain SVG drawing the first N lines and `...` (recommended: it opens anywhere and is
   honest about the cut); (b) keep the box (it renders in browsers only, frozen where it was scrolled).
6. **Q6. The wheel.** (a) The browser's own behaviour (recommended, if the manual check of 7.5 finds gestures latched to
   the page); (b) if it finds readers caught: the box takes the wheel only after the pointer has rested on it or it was
   clicked, a small script.
7. **Q7. Huge payloads.** (a) Box the whole text (recommended: the largest real payload measured costs 10 to 18 ms,
   once); (b) above 20,000 lines, box the first 20,000 and point to "Open box text in new tab" for the rest; (c) draw
   only the lines in view.
8. **Q8. Print.** (a) A box prints as it stands (recommended, documented); (b) a print rule that draws the first N
   lines, like a copy (Q5).
9. **Q9. Scroll position across a redraw.** (a) Kept (recommended); (b) back to the top.
10. **Q10. Section 6 as its own patch (R0) before R1** (recommended), or inside R1.
11. **Q11. The page's SVG cache** is never trimmed: measure its growth over a long session of toggles on a large report
    and, if it matters, bound it like the engine's. Not part of this plan unless asked.
12. **Q12. The keyboard.** (a) Each filled box is a Tab stop (recommended: axe's `scrollable-region-focusable` rule asks
    it of a scrolling region, and it is the only keyboard way through a long payload); (b) the accessibility note's
    model (CN:2711-2714): one Tab stop per diagram, with the arrow keys moving between its notes and boxes, built with
    that note's other work.

## 10. Log

- 2026-10-21: written by session `kronikol-93` at the owner's request. Four read-only agents mapped the drawing
  pipeline, the readers of a drawn note, the option plumbing with the docs and the port, and the tests; every line the
  plan uses was re-read by hand (`research.md`). The prototype and probes in the harness took M1 to M10 the same day in
  Chromium, Firefox and WebKit.
- 2026-10-21, review: an independent review read the first version against the code and raised 22 findings, all taken.
  Those that changed the design: (1) the cap makes different states draw the same source, while the page skips a
  redraw whose drawn source is unchanged and keeps the old picture when a redraw fails, so the records now travel with
  the picture (4.2); (2) "Copy/Open current box text" read the drawn source and would have copied the transparent rows;
  (3) FocusFields emphasis is the generator's markup inside the body, so the box draws it and the widest line is
  measured; (4) the first capped figure was taken with the report's built-in start (YAML view, headers hidden), not the
  designed shape: re-measured as M1b, 3 parts where it said 2; (5) the top-right buttons floated over a box's
  scrollbar, and hidden buttons took clicks; (6) on by default is a major under CLAUDE.md, so the plan recommends the
  option off until the next major; (7) two signatures are pinned, so the cap is a step of its own. The rest: F3's fix
  runs against the owner; Truncated changes for limits above N; T13 is a control; `canWiden`; M9 labelled an estimate;
  numbers re-checked; the Tab-stop departure and Q12; F6's second comment; the pins sorted; test sizes in drawn lines,
  with notes of 40 and 41 lines, T3's oracle and T12's wording; the probes launch through `launchInstalled`; `Overlay`
  and the range check; the Inline SVG wiki page; three corrections to `research.md`; and smaller wording.
- 2026-10-21: 5.2.2 was published (`4a0dad9e`) while the plan was written; every line is at 5.2.2, mapped from 5.2.1 by
  the hunks of `git diff` and re-read where a hunk touched the cited lines.
- 2026-10-21, second check: a read-only pass over every cited line at 5.2.2, each finding re-read by hand. It corrected
  six ranges or their sources (two swap ranges, the font's source, the unescape functions, `lookClone`'s bounds, the
  wiki's start-state table) and four claims: a note's own redraws never call `recordDrawn`, so R1 adds it there (4.2);
  F1's fix moves three pins; F3 also hits the first drawing of a report that starts Wide; and a seventh defect, F7, in
  the branch F2 fixes. The per-line cost of a box was corrected the same day to the figures without
  `content-visibility`, which the design does not use (10 to 18 ms for the largest real payload, where the first version
  said 15 to 40). Nothing in a package changes.

## Appendix A. The harness

`LONG_NOTE_SCROLL_PLAN.harness/`:

| Path | What |
|---|---|
| `research.md` | the code map: drawing, readers of a drawn note, option plumbing, renderers, tests, docs, the port |
| `measurements.md` | M1 to M10, with the commands that took them |
| `gen/gen.cs` | `dotnet run --file gen.cs -- <absolute out dir>`: writes the probe reports (mixed, one-huge, six, their capped twins, Truncated and Expanded) with the published `Kronikol` 5.2.1 package; no build of the repository |
| `gen/gendesigned.cs` | M1b: the 40-call report capped as section 4 designs it, beside the same calls uncapped, both in the JSON view with headers shown |
| `gen/genyaml.cs` | the M10 report |
| `probe/probe.js` | M1: parts, heights, elements, time to draw |
| `probe/proto.js` | the first prototype: draws a capped source beside the report's own diagram |
| `probe/proto2.js` | M2, in the report: boxes over capped notes, then zoom, wheel, find, selection, focus, hover, menu, frames |
| `probe/proto3.js` | M3: narrow window, Ctrl+wheel and drag-to-pan over a box |
| `probe/scale.js` | M4: boxes at scale, `pre-wrap` or `pre`, filled at once, lazily or under `content-visibility` |
| `probe/enginewidth.js` | M5: the engine's width handles |
| `probe/taint.js` | M6: PNG export of an SVG holding a box |
| `probe/latch.js` | M7: wheel latching |
| `probe/kbd.js` | M8: keys on a focused box with the exemption |
| `probe/notesizes.js` | M9: aggregates of a real report's notes and the split estimate (prints numbers only) |
| `probe/repro-yaml.js` | M10: F1 |

The probes take Playwright from `KRONIKOL_PW` (default `probe/pw/package`): point it at the E2E project's bundled driver
(`tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package`, whose Chromium, Firefox and WebKit builds
Playwright installs), or copy that folder to `probe/pw/package`. Every probe launches its browser through
`launchInstalled` from `tools/playwright/installed-browser.js`, so even a run pointed at an installed Chrome or Edge does
not cost the Windows account a sign-in (CLAUDE.md). Reports go to a directory outside the repository.
