# REPORT_UI_FIXES_PLAN: nine report fixes, two of them new controls

**Status: green-lit 2026-10-26, being built.** The owner: "Implement the plan in full", with the recommended answer taken
for every question of section 8, and one more item, I9 (section 4.10): the filter box stops calling its filtering a
search. Written 2026-10-26 at the owner's ask (section 1). Every `file:line` is at `main`
`37428293` (5.9.0) unless it says otherwise. The probes of section 3 ran on reports written by 5.8.0 (`29e551f3`); 5.9.0
added the diagram menu's PlantUML editor and changed nothing they measure.

## 0. In short

Nine asks, each traced to its cause in the code; the first eight reproduced on a report Kronikol 5.8.0 wrote (section 3):

| | Today, measured | Cause | The fix |
|---|---|---|---|
| **I1** chips | a click cycles has, has not (red), off | one tri-state cycle for every row (`report-filter-chips-script.js:138-144`) | two-way by default; a **2-WAY / 3-WAY** switch beside each row's ANY/ALL brings back today's three states; a red chip always means its row is three-way, so old links and Happy paths' Hide still work (4.1) |
| **I2** flash | after a note's own expand or collapse, a diagram drawn in parts shows 1 frame of square yellow notes and black step bars (light and dark), where the report's own controls show none | that path shows the new parts before the colour pass runs, which then waits for an IntersectionObserver that reports after a paint (CN:2735-2750, RS:2405) | colour before showing on every path, and a ResizeObserver, which reports before the paint, for pictures that get their box later (4.2) |
| **I3** loops | hiding Databases leaves both database loops as empty boxes; hiding Steps too leaves an empty Setup box | the line filters remove calls and never the `loop` / `end` around them (CN:3128-3134, 3012-3112) | one pass after the filters drops any block they emptied, its parent too, and nothing else (4.3) |
| **I4** names | `Feature: Month's reports via gRPC` heads its feature as "Month S Reports Via G RPC" | the ReqNRoll lane runs the feature title through a class-name humaniser (`ScenarioInfoEnumerableExtensions.cs:82`, `StringCasing.Titleize`) | Gherkin feature titles as written, as the Cucumber lane already shows them; `Titleize` keeps an apostrophe inside a word; the scenarios keep their ids (4.4) |
| **I5** slider | its thumb 1.5 px under the bar; with "This scenario" open, over the settings' top row | it is placed `bar + 8` and knows nothing of the settings (CM:1639-1647) | placed `+ 11` below whatever floats above it, bar or settings: 4.5 px clear (4.5) |
| **I6** step bars | a table sits 33 px left of its bar's centre; cells have no room | no alignment is set (the engine's default is left); cells get one space | `HorizontalAlignment center`; `<space:8>` and zero-width struts give exactly the HTML table's `padding: 3px 8px`; doc strings stay a framed block; the header centred by the page, as the HTML (4.6) |
| **I7** phone | the report's YAML and WRAP selects 80 px wide in a 313 px sheet; the Timeline's buttons narrow | a desktop rule outranks the phone rule (`stylesheets.css:805-808`); no phone rule for the Timeline | one CSS rule each (4.7) |
| **I8** trend | the rings wrap onto 9 rows at 1000 px, oldest first | the rings are a grid, the bars a scroller (`history-styles.css:147-195`) | one row that scrolls, like the bars; an **Order: Newest first / Oldest first** switch, newest first by default, in markup and CSS only (the labs page runs no script), measured working in Chromium, Firefox and WebKit (4.8) |
| **I9** words | the filter box's text field says "Search... (@tag, ...)", its title "Search names, steps, ...", its help "How to write a search" | the field was named for 4.x's search bar (`ReportGenerator.cs:1926`, `:3110-3113`, `:1944`) | the words a reader sees say filter: the field, its title, its help, the readouts and the wiki; no identifier changes (4.10) |

Twenty more defects were found on the way (section 5), each with its fix and its test. Among them: a service named
"Group" or "Loop" breaks the diagram splitter and the Setup filter (shown), and a Gherkin scenario ending in brackets is
shown as a parameterised group with an "Arg 0" column (shown). Read in the code, with the failing test to come first: a C#
split can write an empty Setup box, the width estimate is never reset (so a wide test splits into one diagram per call
after its first split), and the filter rows' ANY/ALL buttons miss the phone's 36 px touch size.

Releases (section 7): **R1, a patch** (I2 to I7, I9 and the found defects), then **R2, a minor** (I1 and I8, each a new report
control), after 5.9.0, which is on `main` now. The owner's choices are in section 8; the ones that shape the work most are Q1
(what Happy paths' Hide does on a two-way row), Q3 (the switch's look: the words are recommended over the lines, which
read as `=` and `≡`), Q5 (keeping the renamed features' ids), Q14 (two releases or one) and Q16 (whether R2's changed
defaults are a minor, as recommended, or wait for v6).

An independent review of the draft (25 findings) was checked against the code and folded in: section 9 lists what it
changed.

## 1. The ask

The owner, 2026-10-26, verbatim:

> - The default of the filters for Dependencies/Tags switches should be changed to a two way on/off rather than
>   include/exclude/off (ie the exclude indicated by red should only appear when three way mode is on). There is a
>   similar switch next the ANY/ALL button, that switches from 2 way switch to three way. This should have a clear
>   tooltip. I was thinking it could have 2 lines or three lines horizontal on it to indicate 2 way vs three way, but
>   if you think of anything more clear than use that. Be careful of 2/3 vertical lines as 2 vertical lines might look
>   like an or symbol to a programmer.
> - When you expand out or contract the diagram notes of notes with very large payloads in diagrams with large
>   payloads, the notes and steps flash briefly to their default plantuml colours (yellow/black). That needs to be
>   fixed
> - Currently if there are multiple repeated identical calls eg to a database, they're wrapped in plantuml loop
>   syntax, and appear as a loop box in the sequence diagrams. Unfortunately currently when those calls are hidden (eg
>   if databases is toggled off) the loop boxes are still there, empty, which confuses the user. They should toggle on
>   and off along with their contents
> - Can we take into account things like single quotes in the scenario names. Currently `Month's` in gherkin becomes
>   'Month S'
> - The zoom slider on the diagrams could do with having slightly more space between it and the floating payloads
>   panel above, maybe 2-3px, whatever makes sense visually and consistency-wise. Also, when the 'This scenario' panel
>   is toggled on, it should be floating underneath that, not behind it.
> - Can we have the text in the diagram steps fully center aligned, and when there are tables in them, they should be
>   fully centered too. Also each TH and TD could do with more internal padding around the text, if that's possible.
>   The yshould be looking visually close to the ones in the step-param-table.
> - In mobile view, the report level dropdowns for YAML/WRAP should be full width, like their scenario level
>   counterparts. Likewise the radio buttons for SHOW/WARMUP in scenario timeline tab should be full width in mobile
>   view.
> - 'PASS RATE, OLDEST RUN FIRST' diagrams should stay on a single line and have a horizontal scrollbar when there
>   isn't enough room, just like 'TOTAL DURATION'. Also you should be able to toggle between oldest run first and
>   newest run first, with newest run first being the default.

The items are numbered I1 to I8 in that order below. With the green light the owner added a ninth, I9, verbatim:

> Oh one other thing is to make sure the plan includes that we stop referring to filtering in the filter box asw
> searching

## 2. What happens today

### 2.1 I1, filter chips

Two rows have chips and an ANY/ALL switch, **Tags** and **Dependencies**, emitted together in
`ReportGenerator.GenerateHtmlReportCore` (`Reports/ReportGenerator.cs:2002-2014` and `:2017-2027`), outside the
`includeTestRunData` block, so `TestRunReport.html`, `Specifications.html` and a merged report all carry them. Each row
is `span.fltr-controls`: the ANY/ALL button (`FilterModeToggle`, `:3054-3056`), one chip per name (`FilterChip`,
`:3027-3028`), a "none" chip (Untagged, No dependencies), the Tags row's `N more` chip, and the row's Clear. The
**Happy paths** row (Show, Only, Hide, `:1994-1995`) is not a third chip row: its three buttons set the Tags row's
`Happy Path` chip to off, has and has not (`report-filter-chips-script.js:154-159`, decision D8 of 5.0.0). Status is a
set of on/off buttons and Duration one choice; neither is three-state.

**The state model** is in `Reports/report-filter-chips-script.js` (184 lines). A chip's state is two classes per row
(`tag-active` / `tag-exclude`, `dependency-active` / `dependency-exclude`, `FILTER_CHIP_ROWS`, `:10-12`) mirrored in
`aria-pressed` `true` / `mixed` / `false` (`set_chip_state`, `:34-41`). The cycle (`:138-144`):

```js
// off, has, has not, off; toward has not, has not then off
function cycle_chip(chip, toExclude) {
    var s = chip_state(chip);
    var next = toExclude ? (s === 'out' ? 'off' : 'out') : (s === 'off' ? 'in' : s === 'in' ? 'out' : 'off');
```

A plain click goes off, has, has not, off. Alt+click and a right-click go straight to has not (the right-click handler
suppresses the browser's menu, `:177-183`). Enter and Space are the button's own clicks; there is no keydown handler.
ANY/ALL combines the has chips only; a has-not chip always means "none of these" in either mode (`chip_match`,
`:78-88`), and Happy Path set to has is ANDed in whatever the mode (5.2.7).

**What a reader sees.** A has chip takes the selection colours, a has-not chip a red tint over three tokens,
`--kron-exclude-soft`, `-edge`, `-ink`, light and dark (`stylesheets.css:530-532`; `report-tokens.css:49-51`, `:166-168`,
`:271-273`). The red is the only signal of has not, with no glyph or strike-through (the Option C preview dropped the
toolbar plan's "three signals", `plans/OPTION_C_PLAN.harness/mock/c-round3.css:33-34`). The ANY/ALL button is "a mode,
not a filter: a small square button before the chips": `padding: 6px 9px 4px`, `border-radius: 6px`, `--kron-field`
ground, `font: 600 11px/16px`, 28 px tall (`stylesheets.css:540-546`), with no `aria-pressed` and no `aria-label` (its
name is its text). Every chip's title is fixed at generation: the count, then

```csharp
private const string ChipClicks = "Click: only these. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once.";
```

(`ReportGenerator.cs:3031`). The switches' titles: "ANY: scenarios with any of the chosen tags. ALL: scenarios with
every one of them. Click to switch." and the Dependencies equivalent (`:2006`, `:2019`).

**Defaults, the link, Clear.** The start mode of each switch comes from the public `ReportToggleDefaults.TagFilterMode`
and `DependencyFilterMode` (`Reports/ReportToggleDefaults.cs:89-96`, both `Or`), seeded into the scripts by token
(`__TAG_MODE_DEFAULT__`, `__DEP_MODE_DEFAULT__`, `ReportGenerator.cs:1488-1502`). **No option governs how many states a
chip has.** The link (`report-url-hash-function.js:130-163`) writes `tags=` / `tagx=`, `deps=` / `depx=`, `hp=1` /
`hpx=1`, and `tagmode=` / `depmode=` only when the mode differs from the seeded default; reading it
(`apply_url_hash`, `:175-272`) resets every filter first (`:197`) and then sets `tagx` / `depx` / `hpx` chips red
(`:241-250`). Back and Forward treat each chip as one control and pass any unknown key through
(`report-navigation-script.js:575-589`, `:586`). Clear All resets the chips to off and the modes to their seeds
(`reset_filters`, `report-export-function.js:3-31`); a row's Clear resets its chips, not its mode
(`report-filter-chips-script.js:166-175`). Nothing is stored: `localStorage` holds the theme alone, which
`PersistentFilterStateReportTests.Report_stores_nothing_but_the_theme` pins (`:37-49`). Export Filtered HTML has no
filter box and reads only visibility (`report-export-function.js:119-146`), and the filter text can exclude on its own
(`!!`, `!!@tag`, `!!dep:x`), so two-state chips take no ability away.

**Why three states.** The owner chose a tri-state component for Tags, Dependencies and Categories on 2026-08-31
(`plans/TOOLBAR_REDESIGN_PLAN.md:23-28`, reasoning `:175-213`), and 5.0.0 built it (D9, D10, D11 in
`plans/OPTION_C_PLAN.harness/inventory/structure-and-behaviour.md:236-278`; `CHANGELOG.md:1723-1727`, `:1737-1741`).

**What pins three states** (section 6 moves each): `FilterPipelineTests.cs` facts at 81-100, 102-115, 133-147, 149-165,
167-185, 249-271, 273-306, 376-389; `TagFilterTests.A_feature_s_tag_filters_every_one_of_its_scenarios` (152-160, a
second click expects the has-not result); `BackForwardFilterTests.F3` (72-91); the chip title text in `FilterRowsTests`
(102-178), `ControlTitlesTests` (34, 397-409) and `TagsMarkupTests` (190-213). Two more put a chip into has not by a
right-click and would stay green while losing what they hold, since a right-click does nothing on a two-way row (C6):
`FilterPipelineTests.A_row_has_a_Clear_while_one_of_its_chips_is_set` (228-247, the right-click at 236) and
`Clear_All_clears_the_qualifiers_the_tri_state_chips_and_the_modes` (357-374, at 363-364). `ViewportSweepTests` opens the rows only at
768 px and below (230-253), so the rows are never measured at desktop widths, a gap the new control falls into.

### 2.2 I2, the colour flash

Abbreviations: **RS** `Reports/plantuml-browser-render-script.js`, **CN** `Reports/collapsible-notes-script.js`, **CM**
`Reports/context-menu-script.js`, **ISS** `Reports/inline-svg-styles.css`.

**Where the report's diagram colours come from.** The engine draws in its own colours: a payload note `#FEFFDD`
(`PlantUml/NotePalette.cs:6-7`), an event note `#CFECF7` (`PlantUmlCreator.cs:1272-1284`), a participant `#E2E2F0`, a step
bar black with white text (`StepBarPlantUml.cs:55`; a bar with a body, `PlantUmlCreator.cs:1257-1267`, which also draws
its table rules white). The page then repaints in two ways. Attribute selectors match the raw SVG at once: the ground,
the ink, `#181818` lines, the Test hexagon, the 5.8.0 loop and Setup colours (ISS:138-165, 180). Notes, participants,
lifelines and step bars are repainted only through role classes (`k-card`, `k-card-event`, `k-fold`, `k-step`,
`k-step-text`, `k-step-rule`, `k-participant-fill`, `k-lifeline`, ISS:119-136) that the role pass `tagRoles` adds in
script (RS:2398-2615). No attribute rule matches `#FEFFDD`, `#CFECF7`, `#E2E2F0` or a black `rect`; 5.0.0 left those
fallbacks out on purpose, since "the role pass covers" them (`plans/OPTION_C_PLAN.md:707-708`). The role pass also
changes geometry CSS cannot: it rounds the notes and hides their folds, and stretches step bars to the drawing's width
(RS:2477-2521, 2558-2607). So a picture the role pass has not reached shows yellow, square, folded notes and narrow black
bars, which is what the owner saw.

**When the role pass runs.** A MutationObserver on the whole document visits every `<svg>` added (RS:2631-2633, a
microtask, so before the next paint). But `tagRoles` refuses a picture with no layout box, `if
(!svg.getBoundingClientRect().width) return false; // not laid out yet: tagged when it comes into view` (RS:2405), and
hands it to an IntersectionObserver with no root margin (RS:2621-2623). An IntersectionObserver reports in a task queued
by the rendering update, so after the frame that first shows the picture has been painted; a picture below the fold is
reached only when it scrolls in. The MutationObserver watches `childList` only, so showing an SVG that is already in the
document (a `display` change, `data-rendered`, a `<details>` opening) never runs it again.

**The path that flashes.** A note's own controls (its buttons, its double-click, its format and width buttons:
`setNoteState` CN:1101-1118, `setNoteFormat` CN:1070-1078, `setNoteWidth` CN:1091-1094, `keepPlace(onCycle)` CN:808-810)
all reach `rerenderWithNoteStates` (CN:2663-2918). For a diagram drawn in parts it fills each new part while
`display: none` (CN:2683-2695), so every new SVG goes to the IntersectionObserver. The swap then shows the parts and
decorates them, and never tags them (CN:2735-2750):

```js
for (var ni = 0; ni < newFrags.length; ni++) {
    newFrags[ni].style.display = '';
    newFrags[ni].classList.remove('puml-fragment-new');
    ...
}
...
for (var pi = 0; pi < fragQueue.length; pi++) {
    if (window._iflowBindLinks) window._iflowBindLinks(fragQueue[pi].el, origSource);
    makeNotesCollapsible(fragQueue[pi].el);
    ...
```

The report and scenario controls (Payloads, the line count, Headers, filters, format, width) go through
`processRenderQueue` instead, whose parts swap calls `afterRedraw`, and `afterRedraw` has tagged since 5.1.1 (CN:3489-3495:
"The card colours now, inside the slice that drew the picture"). The note's own path was never given the same two
lines. Large payloads are what put a diagram into parts: the split estimate passes `BrowserFragmentMaxHeight` (12,000 px,
`Constants/TrackingDefaults.cs:65`) or a note passes 15,000 characters (RS:720-721, 1151-1211). A small diagram takes the
single-picture branch, whose swap into a visible container (`swapPicture`, CN:2648-2652, 2822-2866) the MutationObserver
tags before the paint, which is why only large diagrams flash.

**The same gap elsewhere** (read in code; section 3 measures the first row and the last, and the facts of section 6.2
cover the three between):

| Path | Shown by | Tagged | Flash |
|---|---|---|---|
| a note's own control, diagram in parts (above) | `style.display = ''`, CN:2737 | IntersectionObserver, after the paint | yes |
| the Wide payload width's refine pass after any redraw (`afterRedraw` calls `decorateNow` CN:3496, then `refineNoteWidth` CN:1136, 1148-1182, which calls `rerenderWithNoteStates`) | the path above | the same | yes, after a report control or a first drawing, with Wide on |
| a first drawing whose part lands while an earlier part is undrawn: ISS:48-50 (5.7.0) hides every undrawn part after the first, so the part is `display: none` when its SVG arrives; `onQueueItemDone` sets `data-rendered` (RS:1827) and decorates it untagged (RS:1823-1842) | `data-rendered` | IntersectionObserver | yes, parts finishing out of order, and the too-large retry (RS:1885-1908) |
| a picture redrawn while not laid out (a Sequence view behind another tab, `toggle-script.js:38-43`; a closed scenario in Firefox, CM:1781-1782; the Chromium skipped flex item `stylesheets.css:71-73` records) | when shown | IntersectionObserver | yes, when shown |
| report and scenario controls (queue), single pictures, parts arriving in order | | before the paint | no |

Caches do not change this: `data-k-roles` lives on each `<svg>` element and every cached markup string is untagged
(RS:174-195, CN:2576), so a reuse is tagged afresh and deferred the same way. In the dark theme the frame is worst: the
attribute rules have already turned the note text light (`--dg-ink`, ISS:145) on a still-yellow note.

No test reads a frame before the final one: `DiagramRolesTests` (90-92, 373-389) and `NoteFormatCardColourTests`
(22-44, "a picture drawn out of sight is tagged as it comes into view, so the cards are waited for", 36-39) poll the end
state. `ReportToggleResponsivenessTests` already has the instrument this needs: a frame watcher that asks for the next
frame from inside its own callback (451-485) and reads state at the first frame showing a new picture (613-656), for
the queue path and for buttons, never colours.

### 2.3 I3, empty loop boxes

**What writes a loop.** `CollapseConsecutiveIdenticalCalls` (off by default, `ReportConfigurationOptions.cs:576-585`; on
by default for `kronikol ingest`, `Ingestion/IngestPipeline.cs:428-435`, `Kronikol.Tool/IngestCommand.cs:28-29`) has
`SequenceCollapser` (`PlantUml/SequenceCollapser.cs`) fold a run of at least `CollapseThreshold` (2) identical
request/response pairs into one. Identical means caller, service, method, path and query, GraphQL operation, status and
kind (`KeyFor`, 126-138); a step bar, an assertion, a marker or the Setup/Action boundary breaks a run (165-188), so a
loop never straddles one. `PlantUmlCreator` wraps the one pair (392-401, 432-434):

```
loop ×3 · 12–48 ms
test -[#colour]> orderDb: POST: /orders
note left
...
end note
orderDb -[#colour]-> test: OK
end
```

`DiagramBuilder.OpenLoop` closes any open loop first (`PlantUmlCreator.cs:2148`), so loops never nest; a loop can sit
inside the Setup box, `partition [#colour] Setup` ... `end` (212-213, 2122-2135), which is Kronikol's only other block.
Neither line says what the block holds; a database is known only by its declaration keyword, `database` or
`collections` (`DependencyPalette.cs:81-92`), a broker by `queue`.

**What hides calls.** Every drawing, redraw and stored source goes through one function (CN:3128-3134):

```js
function applyFilters(source, container) {
    var s = applySetupFilter(source, filterShown(container, '_setupVisible'));
    s = applyAssertionFilter(s, filterShown(container, '_assertionsVisible'));
    s = applyStepsFilter(s, filterShown(container, '_stepsVisible'));
    s = applyDatabasesFilter(s, filterShown(container, '_databasesVisible'));
    return applyEventsFilter(s, filterShown(container, '_eventsVisible'));
}
```

Its three callers are the first drawing (`_preProcessSource`, CN:3319-3337), every redraw (`composeNoteSource`,
CN:2375-2390, used by the note path and the queue) and the note-index map (CN:916-936, which pairs notes by content), and
the splitter runs after it. Databases and Events go through `stripParticipantCalls(src, 'database|collections')` /
`'queue'` (CN:3000-3112), which removes the declarations, every arrow to or from a hidden alias, their notes and
`activate` lines, and pushes every other line through untouched (CN:3106-3108), `loop` and `end` included. A Kronikol
loop holds one pair between one caller and one service, so both arrows and both notes go and the `loop` / `end` pair is
left with nothing between. Steps (`stripStepDelimiters`, CN:2977-2979) and Assertions (`stripAssertionNotes`,
CN:2973-2975) remove statements the same way. Only Setup knows about blocks (`stripSetup`, CN:3136-3225, depth-counted
over `OPENS = /^(?:alt|opt|loop|par|break|critical|group|partition)\b/`, CN:3144): it takes the Setup frame out whole,
with any loop in it. **Nothing removes a block a filter has emptied.** The research ran the page's own filter functions
in node on an emitter-shaped source with Databases hidden (`item3-loops.md` section 2.4) and got

```
partition #F6F6F6 Setup
loop ×3 · 5 ms
end
end
test -[#438DD5]> cowService: POST: /milk
cowService -[#438DD5]-> test: OK
loop ×3 · 5 ms
end
```

an empty loop in an empty Setup box, and a second empty loop. The 5.8.0 look draws each as a rounded light box with its
`loop` tab and condition (`DiagramLook.cs:47-49`), the symptom the owner reported. "Copy current PlantUML source" copies
the same filtered source (CM:960-979), so it carries the empty loop too.

**The same gap, other wrappers:**

| Block | Written by | Emptied by |
|---|---|---|
| `loop ×N ...` | Kronikol | Databases, Events (the reported case) |
| `partition ... Setup` | Kronikol (`SeparateSetup`) | Databases or Events when the setup was only such calls; Steps or Assertions when it held only bars or notes |
| `group`, `alt`/`else`, `opt`, `par`/`also`, `break`, `critical` | a user's `TrackingDiagramOverride.InsertPlantUml` (wiki `Diagram-Customisation.md:44`) | any of the four line filters, partly or wholly |
| `box ... end box` around declarations | a user | Databases or Events, which remove the boxed declarations |

The Server, Local (Java) and NodeJs renderers draw the C# source as written and have no hide toggles
(`ReportGenerator.cs:1578-1597` writes them for BrowserJs only), so the bug is a BrowserJs one. No test combines a loop or
a user block with a hide toggle, and none asserts that a block is absent.

**What else reads blocks** (so a fix does not break them): the splitter's `_blockOpenerRx`
(`/^(partition\s|loop\b|alt\b|opt\b|group\b|par\b|critical\b|break\b)/`, RS:893) with `else`/`also` deliberately not openers
(RS:891-892), `splitDiagramSource` closing and re-opening open blocks at a cut (RS:924-994), `scanOpenBlocks`
(RS:1105-1116), `parseTraceUnits` keeping a block with its first pair (RS:838-886), the lined-up outline that keeps every
block line (RS:1334-1396), and `findOverLongStatement` (RS:1629-1695). All of them run after `applyFilters`.

### 2.4 I4, apostrophes in names

`Month S`, with a capital S, comes from `StringCasing.Titleize` (`StringCasing.cs:15-16`, `32-40`, `77-94`), a port of
Humanizer's. Its word pattern takes runs of letters and digits only,
`(\p{Lu}?\p{Ll}+|[0-9]+\p{Ll}*|\p{Lu}+(?=\p{Lu}|[0-9]|\b)|\p{Lo}+)[,;]?` (15-16), so `Month's birthday` is read as
`Month`, `s`, `birthday`; the words are joined by spaces into `Month s birthday` (77-94) and `ToTitleCase` (39) makes it
`Month S Birthday`. The typographic apostrophe (U+2019) goes the same way. `Titleize` alone calls `ToTitleCase`, so a
capital S can only come from it.

On the Gherkin lanes it is not the scenario title that goes through `Titleize`. The ReqNRoll hooks (one Core for
`Kronikol.ReqNRoll.xUnit2`, `.xUnit3` and `.TUnit`) read the scenario title, rule, row name and step text as written
(`Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs:199-227`, `:103`, `:155`), and `CapitaliseTitles` only upper-cases a first
letter (`Reports/StepText.cs:63-87`, `158-172`). Two things are titleized:

| What | Where | Lanes |
|---|---|---|
| the `Feature:` title, the feature's heading | `DisplayName = featureGroup.Key.Titleize()` (`Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs:82`) | ReqNRoll |
| the Examples column headers | `var displayName = titleizeParameterNames ? name.Titleize() : name;` (`Reports/ReportGenerator.cs:3621`, `:3717`; `TitleizeParameterNames` defaults to true) | every lane |

So the owner's `Month S` was a feature heading on the ReqNRoll lane (or an Examples header). The Cucumber ingest lane
shows feature names as written (`Ingestion/Cucumber/CucumberFeatureSynthesizer.cs:307`, `:488`, `:1158`). The ReqNRoll
lane's description normaliser states the aim of the two lanes, "identical report content"
(`Kronikol.ReqNRoll.Core/GherkinText.cs:3-6`, a comment on description text, not a rule for titles); the feature title
is where they differ. No
changelog entry records a decision to title-case Gherkin feature titles; the call predates the 2026-04-18 `src/`
reorganisation (`4d0f433f`).

It does not stop at apostrophes (traced by evaluating the same regexes, harness research `item4-names.md` section 4):

| Written | Shown today (ReqNRoll feature heading) |
|---|---|
| `Month's birthday` | `Month S Birthday` |
| `Don't bake twice` | `Don T Bake Twice` |
| `Order Status via gRPC` | `Order Status Via G RPC` |
| `Order Cross-Field Validation` | `Order Cross Field Validation` |
| `Orders: retry on 503` | `Orders Retry On 503` |
| `Returns 404 (not found)` | `Returns 404 Not Found` |
| `Salt & pepper`, `Order #42`, `v1.2 release` | `Salt Pepper`, `Order 42`, `V 1 2 Release` |
| `Pancakes 🥞 flip` | `Pancakes Flip` |

On real data: 5 of BreakfastProvider's 67 `Feature:` titles are shown differently from how they are written (`Order
Status via gRPC`, `Recipe Summary via gRPC`, `Stream Order Updates via gRPC`, `Order Cross-Field Validation`, `Reporting
EventGrid Webhook`), and filtering by `grpc` misses them, since the page indexes the feature name as shown
(`ReportGenerator.cs:2460` for a plain scenario, `:3534` and `:3557` for a group).

Other lanes titleize names a person wrote too, which the same fix family reaches: BDDfy's `[Story(Title = ...)]`
(`Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs:79`) and LightBDD's feature name, which LightBDD has already turned from
`Customer_s_Orders` into `Customer's Orders` (`Kronikol.LightBDD.Core/FeatureResultExtensions.cs:39`; inferred from
LightBDD's own formatter, not run), and NUnit4's fixture name, which a parameterised fixture fills with its arguments
(`Kronikol.NUnit4/TestContextEnumerableExtensions.cs:26`). A string response status is titleized into the diagram's
source (`PlantUmlCreator.cs:1172`). xUnit and MSTest titleize a class name (`ScenarioTitleResolver.cs:57`,
`Kronikol.MSTest/TestContextEnumerableExtensions.cs:22`), which holds no punctuation.

Features are ordered by display name everywhere a roster is read (`History/HistoryRunBuilder.cs:52-56`, the same order as
the features JSON, `Failures.md` and CTRF, `ReportGenerator.cs:5357`, `:6234`, `:6621`), so a feature whose display name
changes can move, and with it the `sN` addresses of `kronikol query`.

**The stable id hashes the feature's display name.** `ScenarioStableId.Compute` is SHA-256 over
`"{suite}::{featureName}::{outlineId}::{name}::{k=v|...}"` (`Reports/ScenarioStableId.cs:37-57`), and `For` is called with
`Feature.DisplayName` at 15 sites (`ReportGenerator.cs:2259, 2499, 2988, 3508, 3684, 3792, 5369, 5962, 6254`;
`HistoryRunBuilder.cs:58`; `CtrfReportGenerator.cs:179`; `FailuresDigestGenerator.cs:267`; `LabsReportGenerator.cs:113`;
`MergeableReportMerger.cs:376`; `RunSummaryConsoleWriter.cs:148`). Showing a title as written therefore gives every scenario
of an affected feature a new id: new `#sid-` links and, in cross-run history, the old scenarios read as removed and the
new ones as new. `kronikol history record` suggests a rename only when feature and name are both unchanged
(`Kronikol.Tool/HistoryCommand.Maintenance.cs:301-311`), so it would not offer these. #101 kept ids across a name change
by hashing the old name (`Scenario.KeyedByOutline`, `Scenario.cs:111-114`; `ScenarioStableId.cs:59-66`); `CapitaliseTitles`
(`ReportConfigurationOptions.cs:674-675`), 3.0.47 and 4.16.3 accepted new ids and said so.

What pins today's names: the feature-name theories `ToFeatures_titleizes_feature_display_name`
(`ReqNRoll/ReqNRollScenarioInfoExtensionsTests.cs:420-430`, `ReqNRoll/ReqNRollTUnitScenarioInfoExtensionsTests.cs:230-242`)
with rows `AlternativeEvidenceScenarios`, `OrderProcessing`, `my_feature_name`; BDDfy (`BDDfy/BDDfyScenarioInfoExtensionsTests.cs:285-297`)
and LightBDD (`LightBDD/FeatureResultExtensionsTests.cs:480-494`) likewise; `ScenarioTitleResolverTests.cs:387-412`. No test
has an apostrophe, hyphen or colon in a feature title.

### 2.5 I5, the zoom slider's place

The "floating payloads panel" is the report bar, `.toolbar-row.report-nav`, pinned: on the Features tab its last strip
is the report's display settings, `#display-settings` (Payloads, Format, Toggle; `ReportGenerator.cs:2073-2095`), and
while pinned it is sticky at the top with a shadow (`stylesheets.css:786-789`, `:922-927`). The bar publishes its height
as `--kron-nav-h` (`report-tabs-script.js:75-90`, a `ResizeObserver` plus resize).

The slider is `div.diagram-zoom-controls` prepended to the diagram box by `addZoomButton` (CM:1559-1607): sticky
`top: 2em; left: 2em; z-index: 10; height: 0`, shown at 0.4 opacity while the box is hovered and fully while the slider
is (ISS:75-105), hidden on a phone (ISS:113-117), so I5 is a desktop matter. Sticky alone cannot follow the window,
because the box's `overflow-x: auto` makes the box its scroll box (5.7.0's B4), so since 5.7.0 a script moves it
(CM:1634-1647):

```js
var bar = parseFloat(window.getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h')) || 0;
var top = parseFloat(window.getComputedStyle(controls).top) || 0;
var down = Math.max(0, Math.min(bar + 8 - (r.top + top), r.height - 2 * top - 24));
controls.style.transform = down > 0 ? 'translateY(' + Math.round(down) + 'px)' : '';
```

That puts the controls' zero-height line 8 px under the bar, with the 6 px input (ISS:107-112) centred on it, so the
input's box starts about 5 px under the bar and Chromium's thumb, drawn larger than the input, reaches about the bar's
edge (derived, measured in section 3). It runs for the hovered box and the selected one only (CM:1648-1655), on scroll,
hover, selection and `addZoomButton`.

**The "This scenario" panel** is `.scenario-settings` (`AppendDiagramSectionHead`, `ReportGenerator.cs:1236-1260`,
"This scenario:" label at its start), opened by its gear (`report-scenario-settings-script.js:21-50`). Open, it sticks
under the bar, `.scenario-settings.is-open { position: sticky; top: var(--kron-nav-h, 0px) }`, at `z-index: 4`
(`collapsible-notes-styles.css:104-110`), about 38 px tall on one row and taller when it wraps. `placeControls` knows
nothing of it, so the slider's line `bar + 8` falls inside the panel's band. The slider (z-index 10) paints over the panel
(z-index 4), at the box's hover opacity of 0.4, across the panel's top row (M-I5), which reads as "behind" it. A pointer
moving straight up the diagram onto the slider keeps it (the slider is on top there, M-I5's walk); one that crosses the
panel beside it ends the box's `:hover`, which drops the slider to `opacity: 0; pointer-events: none` (ISS:83-84) and
clears `hoveredBox` (CM:1311-1315).

The page already measures "the band below the bar and below stuck open settings": `freeTop` in
`report-focus-script.js:16-41` (keyboard focus, 12 px gap), which `placeControls` can follow. No test measures the
slider's gap to the bar or its place against open settings: `GradualZoomTests.The_slider_of_a_tall_diagram_stays_in_the_window_while_the_diagram_does`
(671-705) asserts only `top >= --kron-nav-h`, and `ScenarioSettingsTests` (61-86) the panel's own place.

### 2.6 I6, step bars

A step bar is one `hnote across` statement (`PlantUml/StepBarPlantUml.cs`). A step without a table or doc string takes the
legacy form, `hnote across <<stepDelimiter>> #black:<color:white>` + label (`LegacyPrefix`, :55); one with a body takes the
styled form, `hnote across <<stepDelimiter>><<stepBody>>: ` + label, then a blank display line and the table or doc
string, all one physical line joined with `\n` (`RichPrefix`, :56; `Build`, :67-122). A row is written cell by cell with
one space either side (:125-135):

```csharp
foreach (var cell in cells)
    sb.Append(header ? "|= " : "| ").Append(EscapeCell(cell, budget)).Append(' ');
return sb.Append('|').ToString();
```

so `Given muffins\n\n|= name |= price |\n| Blueberry | 3.50 |` (`StepBarPlantUmlTests.cs:100-102`). The styled form gets
the one style block `PlantUmlCreator` injects when a source holds a `<<stepBody>>` (`PlantUml/PlantUmlCreator.cs:1257-1267`):
`.stepBody { BackgroundColor black; FontColor white; LineColor white }`. Nothing Kronikol writes sets an alignment, a
padding or a font size for a bar; there is no `.stepDelimiter` style at all. On the page the shim draws every bar as a
rectangle (`rnote`) with `<b>` before its label (RS:569-589); Node, IKVM and Server draw the source's `hnote`.

**Alignment, from the engine's source** (the pinned engine is a build of PlantUML master at `f44b054d1`,
`Constants/TrackingDefaults.cs:15-34`; its source is in `C:/Code/plantuml-wasm-f44b`; Local rendering uses the 1.2024.6 jar):

- The note component draws the whole text block centred in the bar (`skin/rose/ComponentRoseNoteBox.java:105`, the
  hexagon's `:113`), so a one-line bar already reads as centred, and the role pass keeps it so when it stretches the bar
  (`DiagramRolesTests.A_strip_runs_the_drawings_full_width_with_its_label_centred`, 293-310, legacy bar only).
- Inside the block each line and each table is placed by the style's `HorizontalAlignment`
  (`skin/AbstractTextualComponent.java:81`, `:91`), which nothing sets, so it is the skin root's `left`
  (`skin/plantuml.skin:12`). In a bar with a body the label starts at the left edge of a wider table, or a narrower table
  sits under the left end of the label; wrapped label lines are ragged.
- Because Kronikol always writes `skinparam wrapWidth`, every stripe is rebuilt by `Fission`
  (`klimt/creole/Fission.java:66-112`) and the sheet's alignment governs every line and table
  (`klimt/creole/SheetBlock1.java:155-193`). So `HorizontalAlignment center` in the `.stepBody` style would centre every line
  and each table as a block, on the pinned engine and on 1.2024.6 (`SheetBlock1.java:151-172` there). Per-line `<c>` tags
  are ignored under a wrap width, are not in 1.2024.6, and would land after the shim's `<b>`.
- **A cell's text cannot be centred by the engine**: `AtomTable` honours right alignment only and draws centre as left
  (`klimt/creole/atom/AtomTable.java:119-139`; 1.2024.6 the same, `:135`).
- **Cell padding** is the global `skinparam padding` (`klimt/creole/legacy/StripeTable.java:161`, `Display.java:698-700`),
  which pads every text block of the diagram, and a table's 2 px above and below is hard-coded (`StripeTable.java:84`).
  `<space:N>` (`klimt/creole/command/CommandCreoleSpace.java:53`, `atom/AtomSpace.java:56`, in 1.2024.6 too) is an exact,
  invisible N px; vertical room has no scoped lever, only zero-width `<sup>`/`<sub>` struts in every cell, about 2.5 px
  above and 3 px below by the engine's arithmetic (`klimt/font/FontPosition.java:43-60`), to be measured.
- A table draws no rects, only `<text>` and `<line>` rules, unless a cell or row has a `<#colour>` prefix
  (`AtomTable.java:106-132`).

**The page's part** (role pass, RS:2452-2600): the bar becomes `k-step` (filled `--dg-step`, ink `--dg-step-ink`), its
rules `k-step-rule` (`--kron-hairline`), and the box its rules enclose gets a ground `rect.k-step-table` (`--dg-step-table`)
and a header `rect.k-step-table-head` (`--kron-strip`, ink `--kron-ink-soft`) down to the second horizontal rule
(RS:2463-2476, 2566-2590; ISS:130-136). Everything inside the bar moves by one `dx` when the bar is stretched to the
drawing's width (RS:2477-2522), so the alignment inside the block is the engine's. The page could centre texts in their
columns (reading the vertical rules) but cannot add vertical room, and does nothing for Node, IKVM or Server pictures.

**The target, the HTML `step-param-table`** (`ReportGenerator.cs:4514-4554`, `stylesheets.css:1366-1388`): `th, td {
padding: 3px 8px; border: 1px solid var(--kron-hairline); font-size: var(--kron-fs-meta) }` (12 px), `th { background:
var(--kron-strip); color: var(--kron-ink-soft); font-weight: 600 }`, no `text-align` rule, so `th` is centred (the browser
default) and `td` starts at the left. The diagram's table today: the same ground, header and rule colours, 13 px with a
700 header, about 3.5 px of room either side of a cell (one space) and none above or below (estimates, measured in
section 3).

**Found reading it:** the role pass builds one table box per bar from every rule in it (RS:2465-2476), so a bar holding
two named tables (the weaver path's inputs and outputs, `Tracking/StepCollector.cs:92-99`, `StepBarPlantUml.cs:92-103`)
gets one ground over both and the name line between them, and only the first header is shaded; a header-only table
(two horizontal rules) gets no header shading (`headH` is 0 when `ys.length` is 2). Kronikol4J emits the legacy bar only
(`kronikol4j-report/.../step/StepCollector.java:84`), so any change here is a ledger line there.

What pins today's bytes: the cell bytes in `StepBarPlantUmlTests` (93-367, about fourteen facts), `IngestPipelineTests.cs:456`,
`CapturedTextEscapeTests.cs:446-464` and the E2E `FullPipelineParameterizedE2ETests` (455-483). Four test sources copy the
`.stepBody` style by hand (`ReportTestHelper.cs:1986-1991` and `:4805-4808`, `DiagramRolesTests.cs:17-58`,
`DiagramLookShimTests.cs:20-26`, `DiagramExportLookTests.cs:20-26`). On paint: `StepTableBarTests` (33-133, the label to
header gap of at least 25 px and no whitespace `<text>` within 30 px under the last row), `DiagramRolesTests` (259-310),
`DiagramExportLookTests` (80-170) and `CapturedTextEscapeTests` E2E (164-178).

### 2.7 I7, phone widths

**The report's YAML and WRAP selects.** At both levels the markup is the same: `<span class="diagram-format">` holding a
`note-format-select` and a `note-width-select` (report: `ReportGenerator.cs:2079-2080`, built at `:1612-1631`; scenario:
`BuildScenarioDiagramToolbar`, `:1169-1191`, `:1176`). The phone sheet (`collapsible-notes-styles.css:137-177`,
`@media (max-width: 768px)`) turns both into a column of full-width zones:

```css
.diagram-options-bar { flex-direction: column; align-items: stretch; gap: 0; margin: 6px 0 12px; padding: 0 14px 12px; }
.toolbar-right { flex-direction: column; align-items: stretch; gap: 0; }
```

(`:150-151`), with `.diagram-format { display: flex; flex-direction: column; align-items: stretch }` stretching the selects
inside the zone (`:160-165`). For the scenario sheet that is the end of it. The report sheet is
`.features-tools .toolbar-right`, and an all-width rule written for the desktop row,
`.features-tools .toolbar-right { display: flex; flex-wrap: wrap; align-items: center; column-gap: 14px; row-gap: 6px; ... }`
(`stylesheets.css:805-808`, specificity 0,2,0), outranks `.toolbar-right { align-items: stretch }` (0,1,0). A column
flexbox with `align-items: center` sizes each zone to its content and centres it, so the Format zone is only as wide as
its widest select. The same rule leaves the report sheet's zones 6 px further apart than the scenario sheet's
(`row-gap: 6px` against `gap: 0`), and from 481 to 768 px all three zones are narrower than the sheet. A page without
tabs has no `.features-tools`; its `.toolbar-right` stretches its zones but is itself shrink-wrapped at the left of a
`flex-direction: column; align-items: flex-start` row (`stylesheets.css:2070-2074`).

**The Timeline's SHOW and WARM-UP buttons.** `TimelineControls` (`ReportGenerator.cs:2906-2922`) writes
`div.timeline-controls` holding up to three `span.tl-seg[role=group]` groups (Show: Filtered, Full; Warm-up: Shown,
Hidden; Order: With warm-up, Without warm-up; the last two only when a row paid a warm-up), each a label and
`button.tl-seg-btn`s (`TimelineSegments`, `:2925-2931`). `.timeline-controls { display: flex; flex-wrap: wrap; gap: 8px 18px;
margin-left: auto }` and `.tl-seg { display: inline-flex }` (`stylesheets.css:1674-1678`); the phone block adds only a
9.5em label column (`:2088-2089`). So on a phone each group stays as wide as its content, at the left, and Order (about
345 px) is wider than a 375 px screen's card and wraps its labels (derived). The Components tab reuses the classes
(`ComponentHeader`, `:2948-2969`), so a rule meant for the Timeline must be scoped to `#scenario-timeline` or reach both,
which section 4 decides.

No fact measures a zone, a select or a segment against its sheet at a phone width. `ViewportSweepTests` opens every
scenario's settings at every width, by the gear a reader has there (`:247`: `.gear-in-summary` at 768 px and below,
`.scenario-settings-gear` above), but never the report's `.tools-gear`, which shows at 768 px and below, so the report
sheet has never been swept at phone widths; it unhides the Timeline panel and checks only that nothing spills.

### 2.8 I8, the pass-rate strip

Both trend cards come from one internal emitter, `History/HistoryHtml.cs`: `PassRateChart` (251-266) and
`DurationChart` (279-302), drawn when the trend holds two or more runs (177-184). Each writes its title from one
constant, `"Pass rate, oldest run first"` (253) and `"Total duration, oldest run first"` (281), into the visible title
and the list's `aria-label` alike; the CSS upper-cases it (`history-styles.css:143`). So **both** cards say "oldest run
first", and both follow `HistoryVerdicts.Runs`, which is oldest first with the current run last. Readers take `Runs[^1]` as
the current run (`HistoryHtml.cs:169`, `Query/QueryCommand.History.cs:299`, `:835-836`, `:945-949`,
`Kronikol.Tool/HistoryCommand.Maintenance.cs:124`), so the model keeps its order and any reversal is the page's.

They lay out differently by construction (`history-styles.css`):

| | TOTAL DURATION (bars) | PASS RATE (rings) |
|---|---|---|
| scroll box | `.history-bars-scroll { overflow-x: auto; overflow-y: hidden; direction: rtl }` (175-179) | none |
| row | flex, no wrap, `width: max-content; min-width: 100%` (180-188) | grid, `repeat(auto-fit, minmax(56px, 1fr))` (147-153) |
| item | `flex: 1 0 auto; width: 48px` (189-195): grows, never shrinks | fixed 56 x 56 (154-162) |
| more runs than fit | one row, scrolls inside the card | `auto-fit` makes as many columns as fit, the rest wrap onto more rows |
| opens on | the newest run, because the scroller is `rtl` and the row inside it `ltr` (178, 181) | (all shown) |

The section card clips (`.history-section { overflow: hidden }`, 81), so a ring row without a scroll box of its own
would be cut off, not scrolled. A full default window is 51 runs (`HistoryWindow` 50 plus this run, no cap on the
charts): 51 rings take 3,356 px, 51 bars 2,748 px.

Where: the report's History tab (on by default since 5.0.0, `HistoryHtml.InTab`, `ReportGenerator.cs:2217`) and the
labs page (`HistoryHtml.Section`, `LabsReportGenerator.cs:69-73`), also both pages `kronikol merge` writes
(`MergeableReportRenderer.cs:135-136`, `:156-161`); never Specifications. **No script touches the cards**: the labs page
runs only the two theme scripts, which three facts pin (`LabsReportTests.cs:114-124`, which also refuses any
`onclick`; `LabsPageTests.cs:79-87`; `HistoryRingsTests.cs:185-193`).

There is no control in the History section today. The nearest pattern is the Timeline and Components segmented buttons
(`ReportGenerator.TimelineSegments`, 2924-2931: `span.tl-seg[role=group]` of `button.tl-seg-btn[aria-pressed]`, the first
one pressed, styled at `stylesheets.css:1653-1694`, a sheet the labs page also loads), driven by script and remembered
nowhere: no toggle default, no address entry, no storage (`report-navigation-script.js:6-7`: display settings add no
history entry). `ReportToggleDefaults` has no History member.

What pins today's shape: `HistoryRingsTests.cs:137-182` asserts the rings wrap (`rings.scrollWidth - rings.clientWidth
<= 0`, "the rings run past their card instead of wrapping onto rows", 180) and finds the oldest and newest bars by DOM
index (176-177); `HistoryRingsTests.cs:56`, `:102` and `HistoryRingsMarkupTests.cs:61-80`, `:87-119`, `:134-167` read runs in
DOM order; `HistoryRingsMarkupTests.cs:169-180` pins both titles. Kronikol4J has no cross-run history (its ledger lists
"the History tab's rings and bars" as not mirrored), so I8 is a ledger line there and nothing more.

## 3. Measured

How (Appendix A has the commands): `gen/gen.cs` runs on the published Kronikol 5.8.0 package and writes, with the
report's own emitter and generator, `main.html` (a BrowserJs report holding every case below), `stepbars.html` (one
diagram in five step-bar forms), `history-51.html` and `history-7.html` (the History section alone, as the labs page
draws it) and `names.txt`. The probes in `probe/` drive Playwright's bundled browsers through the 1.59.1 driver the .NET
package ships (Chromium 1217, Firefox 1511, WebKit 2272, the E2E suite's), at the widths named, light theme unless
dark is named. Pictures and logs are in the session scratchpad, not the repository (they hold nothing secret, but
are large); every number below is in the probes' JSON.

**M-I1, the switch in the real rows** (`probe/i1-switch.js`; the Tags row shows a candidate two-way with one chip on, the
Dependencies row the same candidate three-way, pressed, with one chip on and one left out). At 1280 px, light and dark,
and 390 px:

| Candidate | How it reads at 1x |
|---|---|
| two or three horizontal lines | as an equals sign (`=`) and as the menu icon (`≡`), the two readings the ask worried about for vertical lines |
| a pill of the chip's states | too small to read; the red state is lost on the pressed button's blue ground |
| the words **2-WAY / 3-WAY** | unambiguous, the owner's own terms, and the same kind of button as the ANY/ALL text switch beside it |
| the glyphs ✓○ / ✓✕○ | readable, and names the states' meaning, but busier than the words |

The phone picture also shows E1: ANY/ALL and the switch stand 28 px tall beside 36 px chips.

**M-I2, the flash** (`probe/i2-flash.js`, the generator's long diagram: 40 calls with payloads of 24 to 400 lines and
step bars holding tables, drawn in 2 parts, 80 notes; the trigger dispatched as the E2E tests do):

| Trigger | Theme | Frames showing the redrawn part | Frames in the engine's colours | First such frame |
|---|---|---|---|---|
| a double-click on a note (the note's own redraw) | light | 33 | 1 (2 in an earlier run) | the part untagged: 8 note paths `#FEFFDD`, 1 black bar in view |
| the same | dark | 33 | 1 | the same |
| the report's Payloads: Expanded (the queue) | light | 40 | 0 | |
| the same | dark | 40 | 0 | |

Both runs of each also ended two tasks with an untagged picture shown (the frame-independent check), so the raw frame is
certain, not a sampling accident; it lasts as long as the frame does, about 30 ms here, longer on a slower machine or a
bigger part.

The screencast of the light run caught the frame: square yellow notes with their folds, the black step bar with white
text and white table rules, the engine's lilac participants. In the dark frame the notes are bright yellow with the
dark theme's light ink already on their text, so the payloads are almost unreadable, and the participant names vanish
on lilac boxes. That is the owner's "flash briefly to their default plantuml colours (yellow/black)", and it is the
note path only, as section 2.2 read: the same diagram redrawn by the report's own control shows no such frame.

**M-I3, the loops** (`probe/i3-loops.js`, the Setup box holding a Given bar and a loop of three database calls, then
two HTTP calls, a loop of three database calls and a loop of three broker calls):

| Hidden | Blocks left empty in the drawn source | Loop boxes drawn |
|---|---|---|
| nothing | none | 3 |
| Databases | both database loops | 3 |
| Events | the broker loop | 3 |
| Databases and Steps | both database loops and the Setup box | 3 |

The pictures show the owner's case exactly: a `loop [×3 · 5 ms]` tab over an empty rounded box, and a Setup box
holding only an empty loop.

**M-I4, the names** (`names.txt`, the real functions through the internals; `probe/i4-group.js` on the page):

| Written | `Titleize` (a ReqNRoll feature heading) | `FormatScenarioDisplayName` (an outline heading) |
|---|---|---|
| `Month's birthday` | Month S Birthday | Month's birthday |
| `Month’s birthday` | Month S Birthday | Month’s birthday |
| `O'Brien's order` | O Brien S Order | O'brien's order |
| `Don't bake twice` | Don T Bake Twice | Don't bake twice |
| `Order Status via gRPC` | Order Status Via G RPC | Order status via g rpc |
| `Order Cross-Field Validation` | Order Cross Field Validation | Order cross-field validation |
| `Orders: retry on 503` | Orders Retry On 503 | Orders: retry on 503 |
| `Returns 404 (not found)` | Returns 404 Not Found | Returns 404 [not found] |
| `Order "VIP" lane` | Order VIP Lane | Order "vip" lane |
| `Salt & pepper` | Salt Pepper | Salt & pepper |
| `v1.2 release` | V 1 2 Release | V1.2 release |
| `HTTP request returns 200` | HTTP Request Returns 200 | Http request returns 200 |
| `Pancakes 🥞 flip` | Pancakes Flip | Pancakes 🥞 flip |

On the page, `Feature: Month's reports via gRPC` heads its feature as "Month S Reports Via G RPC". E8 is real: the plain
scenario `Returns 404 (not found)` is drawn as a group titled "Returns 404", 1/1 passed, with an `Arg 0` column reading
"not found", so the scenario's own name appears nowhere.

N2, run on a copy of 5.9.0's `Titleize` and of the change (`gen/titleize.cs`): the runtime's own
`ToTitleCase("month’s")` is "Month’S" and `ToTitleCase("o'brien's")` is "O'brien's", so the change has to keep such a word
away from `ToTitleCase`, as N2 does.

| Written | 5.9.0 | N2 |
|---|---|---|
| `Month's birthday` | Month S Birthday | Month's Birthday |
| `Month’s birthday` | Month S Birthday | Month’s Birthday |
| `O'Brien's order` | O Brien S Order | O'Brien's Order |
| `Don't bake twice` | Don T Bake Twice | Don't Bake Twice |
| `Customer's Orders` (LightBDD's `Customer_s_Orders`) | Customer S Orders | Customer's Orders |
| `HTTP, then the order` | Http, Then The Order | HTTP, Then The Order (E16) |
| `the 'quoted' word`, `rock 'n' roll` | The Quoted Word, Rock N Roll | the same |
| `Order Status via gRPC`, `OrderProcessing`, `my_feature_name`, `v1.2 release`, `3d printing`, `Pancakes 🥞 flip` | Order Status Via G RPC, Order Processing, My Feature Name, V 1 2 Release, 3D Printing, Pancakes Flip | the same |

**M-I5, the slider** (`probe/i5-slider.js` and `probe/i5-thumb.js`, 1600 by 900, the window 1,200 px inside the long
diagram, the bar pinned and 132 px tall):

| | 5.8.0 |
|---|---|
| the slider input's box, below the bar's bottom | 5.4 px |
| the thumb as painted (the topmost accent pixels of a 2x screenshot), below the bar's bottom | **1.5 px** |
| the thumb's height, and its centre's distance from its top | 14.5 px, 6.9 px |
| with "This scenario" open: the settings, stuck under the bar | 132 to 172 px |
| the slider input's box, against the settings' bottom | **34.6 px inside the settings' band** |
| what is drawn at the slider's centre | the slider, over the settings (z-index 10 over 4), at the box's hover opacity 0.4 |

So today the thumb nearly touches the bar, and with the settings open the slider sits on their top edge, faded,
across their "THIS SCENARIO: PAYLOADS" row: the "behind" of the ask. The line the slider follows is `bar + 8`; at
`bar + 11` the thumb stands 4.5 px clear (3 px more than today), at `bar + 10` 3.5 px.

**M-I6, the step bars** (`probe/i6-stepbars.js`, `stepbars.html`: a bar with a table, one with a JSON doc string, one with
two named tables, one plain; the role pass's geometry in screen px):

| Variant | Table's centre against the bar's | Label's centre (two-table bar) | Room left of a cell's text | Above and below | Bar with the 3-row table |
|---|---|---|---|---|---|
| 0, as today | 33 px left | 29.6 px left | 0 (a space inside the text) | 0 | 114 px |
| 1, `HorizontalAlignment center` | 0 | 0.5 | 0 | 0 | 114 px |
| 2, and `<space:8>` each side | 0 | 0.5 | **8 px** | 0 | 114 px |
| 3, and zero-width struts | 0 | 0.5 | 8 px | **3 px, 3 px** | 138 px (6 px a row) |
| 4, 1 with the doc string a one-cell table | 0 | 0.5 | | | |

Variant 3 has exactly the HTML table's `padding: 3px 8px`. Variant 1 centres a doc string's lines one by one, so the JSON
loses its indentation (the second and third lines are centred, not indented); variant 4 keeps the indentation in a framed,
centred block. In every variant a cell's text sits at the left of its column (the median header text is 13 px off its
column's centre), where the HTML centres its header; and the bar with two tables gets one ground and one shaded header,
as wide as the wider table (E10). The HTML table beside them: header centred and shaded, body at the left, 12 px type,
header weight 600. The variants ran on today's role pass, without B4 and B6, which the page does not do yet: variant 4's
box was not shaded because today's role pass shades no header-only table, which B6 changes, so B4's bold-row test is what
keeps it unshaded once B6 lands, and section 6.6's box fact holds the three together.

**M-I7, phone widths** (`probe/i7-phone.js`):

| | 375 px | 600 px |
|---|---|---|
| the report sheet's content width | 313 | 534 |
| its Format zone, and its YAML and WRAP selects | **80, 80, 80** | **80, 80, 80** |
| its Payloads and Toggle zones | 313, 313 (their content is that wide) | 416, 447 |
| its zones' `align-items`, `row-gap` | center, 6 px | center, 6 px |
| a scenario sheet's content width, every zone and select | 279, all 279 | 500, all 500 |
| the Timeline's controls | 309 | 530 |
| its groups (Show, Warm-up, Order) | 220, 237, 309 | 220, 237, 343 |

At 375 px the Order group's buttons wrap their labels ("With warm- / up"). The pictures show the report's Format zone as
a narrow column centred in its sheet, as section 2.7 read.

**M-I8, the trend** (`probe/i8-history.js`, 51 runs):

| | 1000 px today | 390 px today | the design of 4.8 (H2 to H4), in Chromium, Firefox and WebKit, 1000 and 390 px |
|---|---|---|---|
| ring rows | **9** | **13** | 1 |
| rings scroll | no | no | yes (2,924 px at 1000) |
| bars | 1 row, scrolls | 1 row, scrolls | 1 row, scrolls |
| the newest run in view on opening | yes (bars) | yes | yes, in both orders, both cards |
| the page scrolls sideways | no | no | no |
| each list's `aria-label` | "..., oldest run first" | | its own order, in both |

The design works without script in all three engines: native radios, `:has()`, both orders written. Its cost is the
second copy of each list: the two lists are 39,265 bytes of markup for 51 runs today (29,469 the rings, 9,796 the bars),
so the History section grows by that much, before the report's compression; a smaller window costs proportionally less.

## 4. Design

Rules are numbered per item (C for I1's chips, F for I2, L for I3, N for I4, S for I5, B for I6, P for I7, H for I8) so the
tests in section 6 can name the rule they hold. Where a rule rests on a choice that is the owner's, it names the
question in section 8 and is written as recommended.

### 4.1 I1, two-way chips by default, a three-way switch per row

- **C1. A switch per row.** The Tags row and the Dependencies row each get a "ways" switch right after their ANY/ALL
  button, the same small square button family (`.tag-ways-toggle`, `.dep-ways-toggle`). Each row's ways are its own, as
  its ANY/ALL mode is (`_tagMode`, `_depMode`).
- **C2. Two-way, the default.** A click turns a chip on (has) or off. No click reaches has not, so no chip is red. Enter and
  Space are clicks as today.
- **C3. Three-way.** A three-way row gives today's chips exactly: off, has, has not, off; Alt+click and a right-click go
  straight to has not.
- **C4. A red chip means three-way.** A row is three-way while its own switch says so or while any of its chips is red.
  On a row the reader left two-way, a red chip can only come from outside the row: a link carrying `tagx=`, `depx=` or
  `hpx=1`; Happy paths' Hide, which is the Happy Path chip's has not (D8 of 5.0.0; Q1 has the alternatives); Back and
  Forward to such a state. So the owner's rule ("the exclude indicated by red should only appear when three way mode is
  on") holds for every way in, and the row is two-way again once its last red chip goes (Happy paths' Show, a click,
  the row's Clear).
- **C5. Turning three-way off** (a click on the switch while the row is three-way) sets the row's own ways to two-way and
  every red chip of the row to off (Happy paths back to Show when it was on Hide), filters again and rewrites the link.
  Turning it on changes no chip.
- **C6. Right-click and Alt+click on a two-way row** are a plain click and the browser's own menu: the chip's
  `contextmenu` handler (`report-filter-chips-script.js:177-183`) and the click handler's Alt (`cycle_chip(chip,
  e.altKey)`, `:151`) act only on a three-way row. (Q2: or have them leave the chip out and turn the row three-way.)
- **C7. The link, and Back.** `tagstates=3` and `depstates=3` record the reader's own switch only, written while it is on
  (two-way is the default and is never written, as `tagmode=` is written only off its default), and are read back by
  `apply_url_hash`. A red chip's three-way is read from `tagx`, `depx` and `hpx` (C4) and never written as `tagstates`,
  so every link written by 5.0.0 to 5.9.0 keeps its meaning, and Happy paths' Hide then Show, or the first chip change
  after an old link, change the same keys as they do today. That keeps the navigation script as it is: Back and Forward
  make one entry of a run of changes to one control and one of a change to several at once
  (`report-navigation-script.js:575-608`). A switch flip is a control of its own (an unknown key is, `:586`), so it is one
  Back step; a flip that also clears red chips changes several keys, one entry.
- **C8. Clear All** resets both rows to two-way, as it resets the modes (`reset_filters`, `report-export-function.js:3-31`).
  A row's Clear keeps the row's own ways, as it keeps its mode; a row that was three-way only for its red chips is
  two-way after it.
- **C9. Titles say what a click does in the row's current ways** (D49's "count, then what a click does"). A chip's title
  is generated in the words of the row's start ways, two-way: "N scenarios. Click: only these. Click again: off."; the
  script rewrites every chip title of its row when its ways change, keeping the count (written as `data-scenarios`, so
  the text is rebuilt, never parsed). The switch is a button of the ANY/ALL kind: its text says the row's current ways,
  **2-WAY** or **3-WAY**, and is its accessible name, with no `aria-label` and no `aria-pressed`, as ANY/ALL has neither.
  A toggle button keeps one label in both states when it carries `aria-pressed` (the WAI-ARIA button pattern), and WCAG
  2.5.3 Label in Name wants the visible text inside the name, so a label that says the state takes neither. Its title names
  the state and the click:
  - two-way: "Two-way chips: a click shows only a chip's scenarios, a second click shows every scenario again. Click for
    three-way chips, where the second click leaves the chip's scenarios out (red) and a third shows them again."
  - three-way: "Three-way chips: a click shows only a chip's scenarios, a second leaves them out (red), a third shows every
    scenario again. Click for two-way chips."
- **C10. The look.** The words **2-WAY** and **3-WAY**, on a button of the ANY/ALL switch's own kind and size (28 px; 36
  px on a phone, C11), in the selection colours while three-way. M-I1 put four candidates into the real rows: the
  owner's two or three horizontal lines read as an equals sign and as the menu icon, the very confusion the ask warned of
  for vertical lines; a pill of the chip's states is too small at 1x and loses its red on the pressed ground; the glyphs
  ✓○ / ✓✕○ read, but are busier. The words are the owner's own terms and sit beside a switch that is words too (Q3, with
  the pictures).
- **C11. Touch.** The phone's 36 px rule for every filter control (`stylesheets.css:2202`) lists the switch, and with it the
  ANY/ALL buttons and the row's Clear, which miss it today (section 5, E1). That rule stands in a 480 px block (`:2168`),
  where the wiki and the page's one phone breakpoint say 768 px (E19, Q17).
- **C12. Where.** The rows are the generator's (`ReportGenerator.cs:2002-2027`), so `TestRunReport.html`,
  `Specifications.html` and a merged report all get the switch. The filtered export has no filter box and reads only
  visibility, so it needs nothing; the filter text's own `!!` exclusions are untouched.
- **C13. No new option.** Nothing asks for a configured start state; `ReportToggleDefaults` would take a member
  (`TagChipWays`, `DependencyChipWays`) only if the owner wants one (Q4). The seeds go into the scripts the way the modes'
  do (`__TAG_WAYS_DEFAULT__`, `__DEP_WAYS_DEFAULT__`, `ReportGenerator.cs:1488-1502`), and the generator writes the
  switch's text and title and the chips' titles from the same value, as it writes ANY/ALL's text from the resolved mode
  (`:2006`, `:2020`). An option added later is then the resolver and one fact that a three-way start shows its words in
  the first frame.

Code: `ReportGenerator.cs` (`FilterWaysToggle` beside `FilterModeToggle`, `:3054-3056`; the two `ChipClicks` texts, `:3031`;
`FilterChip`'s `data-scenarios`, `:3027-3028`; the comments at `:1949-1953`, `:3021-3026`), `report-filter-chips-script.js`
(the row's ways, `cycle_chip`, the right-click handler, `set_ways`, Happy paths' Hide), `report-url-hash-function.js`
(write and read `tagstates` / `depstates`), `report-export-function.js` (`reset_filters`), `stylesheets.css` (the switch,
its three-way colours, the 36 px list), the violet theme's hover edges (`Constants/Stylesheets.cs:136-144`, which
`StylesheetRulesTests` keeps before every active state).

### 4.2 I2, no picture shown before its colours

- **F1. Colour before showing, on every path.** One helper, `tagShown(el)` (the role pass on every SVG under `el`, from
  `window._diagramRoles`), is called wherever a drawn picture is shown: the note path's parts swap right after
  `style.display = ''` and before `makeNotesCollapsible` (CN:2735-2750), and `afterRedraw`, which already does it
  (CN:3494-3495), so the two paths cannot drift again.
- **F2. The first drawing.** `onQueueItemDone` (RS:1823-1842) calls it after `data-rendered` shows the part and before the
  part is decorated, for the parts that land out of order (ISS:48-50).
- **F3. A picture that gets its box later is coloured in the frame it gets it.** `tagRoles` refuses a picture with no
  box (RS:2405), so the waiting list waits for a box, today on an IntersectionObserver (RS:2621-2623), which reports
  after the first frame the picture is in the window has been painted. The waiting list moves to a ResizeObserver, which
  reports after layout and before paint in the frame a picture first gets its box, so the picture is tagged there,
  wherever it is; each is unobserved once tagged, as today (RS:2622), and the IntersectionObserver stays only where there
  is no ResizeObserver. Nothing is left to wait for the window, so no scroll, link or jump can reach an untagged picture.
  What gets a box in one frame is bounded: a scenario far from the window is skipped (`.scenario`'s `content-visibility: auto`,
  `stylesheets.css:91-99`) and is laid out, giving its pictures their boxes, only as it nears the window, so a frame tags
  at most the diagrams just shown or just reached, as `afterRedraw` tags every part of a diagram at once since 5.1.1. That
  covers a Sequence view drawn behind another tab, a closed scenario in Firefox, the Chromium skipped flex item
  (`stylesheets.css:67-73`) and any path added later.
- **F5. Which instrument sees which rule.** In a frame, animation-frame callbacks run, then style and layout, then
  ResizeObserver callbacks, then paint. F1 and F2 tag inside the task that shows the picture, so a frame watcher sampling
  in `requestAnimationFrame` (`ReportToggleResponsivenessTests` 451-485) and a check at the end of every task that
  changes the box both see them. F3 tags in the page's ResizeObserver, after any animation-frame sample: a sample there
  would count a picture that is never painted raw. F3's facts sample in a ResizeObserver the test creates after the
  page's (callbacks run in the order their observers were made), so they read each frame as it will be painted.
- **F4. No attribute fallbacks.** Painting `#FEFFDD`, `#CFECF7`, `#E2E2F0` and black rects by attribute (5.0.0's preview,
  `plans/OPTION_C_PLAN.harness/mock/c-round5.css:95-97`) would fix colour only, not the rounding, the folds or the bar
  stretch, and would repaint a participant a user declared in one of those colours. 5.0.0 rejected it; F1 to F3 make it
  unneeded.
- **Cost.** F1 moves the role pass's work for the new parts into the swap task, as the queue's 5.1.1 change did; F3 moves
  a waiting picture's work from just after its first frame in view to the frame it gets its box, bounded as F3 says. The
  time is recorded per picture (`data-k-roles` `ms`, RS:2612-2613) and is held to `ReportToggleResponsivenessTests`'
  budgets (section 6).

### 4.3 I3, a block goes with what it holds

- **L1. Drop what the filters emptied.** `applyFilters` (CN:3128-3134) keeps the Setup filter's result, `s1`, runs the
  other four filters to get `s`, and, when `s` differs from `s1`, returns `dropEmptiedBlocks(s1, s)`. Only the Setup filter
  removes block lines, and it removes whole frames (`stripSetup`, CN:3136-3225); the four line filters never remove an
  opener or an `end`, so the k-th opener of `s` is the k-th opener of `s1`. A block is dropped when it holds nothing in `s`
  and held something in `s1`, so a block a user wrote empty is left as written, and a source no filter changed is returned
  as it came (which keeps `_preProcessSource`'s early return, CN:3335).
- **L2. What a block is**, read the way the engine reads it (`CommandGrouping.java:145-153` in the pinned engine's
  source): an opener `^&?\s*(?:opt|alt|loop|par|par2|break|critical|group|partition)\b` in any case, closed by `end` alone
  or followed by text other than `box`, `note`, `hnote`, `rnote` or `ref`, whose own blocks those are; `else` and `also`
  separate branches and are neither content nor openers. Arrows are tested first, so a participant whose alias is
  `group` or `loop` stays a message (section 5, E5). `box` ... `end box` is a block of declarations. Note bodies (to
  their `end note`, `end hnote`, `end rnote`, a body line `end` included) and `<style>` blocks, one-line and multi-line,
  are skipped, with the render script's rules (RS:733-760) and `stripSetup`'s (CN:3147-3158).
- **L3. What is content:** an arrow, a note of any kind (one-line or block, a step bar, an assertion note, a row band), a
  `ref`, a divider `== x ==`, a delay `...`, a spacer `|||` or `||N||`, `destroy`, `return`, and a block that is kept;
  inside a `box`, a declaration. Not content: blank lines, comments (the `'__^*__` location comment an assertion leaves,
  `Track.cs:366-370`, must not keep a block alive), `activate`, `deactivate`, `create`, `autonumber`, `hide`,
  `skinparam`.
- **L4. What goes:** the opener, any `else` / `also` lines, and the closer. Lines between them that are not content
  (`activate`, comments) stay where they are, so nothing unbalances. A parent whose only content was a dropped block goes
  too (a stack), which is how an emptied loop in the Setup box takes the emptied Setup box with it, and a `box` whose
  declarations the Databases or Events filter removed goes with them. A block with content in any branch is kept whole,
  its empty branches included, since dropping an `else` label changes what the diagram says.
- **L5. Every reader follows.** All drawing, every redraw, the note-index map, "Copy current PlantUML source" and 5.9.0's
  "Open current PlantUML in editor" read `applyFilters`' result (section 2.3), and the splitter, the lined-up outline and the length check all run after it, so one
  pass fixes them all. It is linear, one scan of each source.

### 4.4 I4, names as written

- **N1. A Gherkin feature heading is shown as written.** `ScenarioInfoEnumerableExtensions.cs:82` takes
  `featureGroup.Key` as it is, as the Cucumber lane does (`CucumberFeatureSynthesizer.cs:307`), so the two lanes give the
  same heading; `CapitaliseTitles` still capitalises the first letter. That fixes `Month's`, `Don't`, `gRPC`,
  `EventGrid`, hyphens, colons, `via`, parentheses and emoji in one change. Features are ordered by display name (section
  2.4), so a renamed feature can move on the page, in `Failures.md` and CTRF, and in `kronikol query`'s `sN` addresses;
  the changelog says so.
- **N2. `Titleize` keeps a word with an apostrophe in it as written** (`'` or U+2019 between letters), for every caller
  that titleizes words a person wrote or a formatter produced: BDDfy's story titles, LightBDD's feature names (which
  LightBDD itself has already given their apostrophe), NUnit4's fixture names, Examples headers and a string response
  status (section 2.4). The word pattern (`StringCasing.cs:15-16`) first takes a run of letters joined by apostrophes,
  `\p{L}+(?:['’]\p{L}+)+`, as one word. Such a word keeps its letters as written but for its first, which is
  upper-cased, and never goes through `ToTitleCase`, which reads U+2019 as a word break ("Month’S") and lower-cases what
  follows a U+0027 ("O'brien's"). Every other word is cased as today. Measured with a copy of the function
  (`gen/titleize.cs`, section 3, M-I4): `Month's birthday` gives "Month's Birthday", `Month’s birthday` "Month’s
  Birthday", `O'Brien's order` "O'Brien's Order", `Don't bake twice` "Don't Bake Twice", and quotes around a word
  (`the 'quoted' word`) change nothing. The doc comment "Equivalent to Humanizer's Titleize()" (`StringCasing.cs:30`)
  becomes "Based on". Kronikol4J ports `titleize` (`kronikol4j-core/.../naming/StringCasing.java:28-31`): a ledger line,
  or the same change there.
- **N3. Ids.** A feature whose display name N1 or N2 changes gives each of its scenarios a new stable id, since the id
  hashes the feature's display name (section 2.4): on the ReqNRoll lanes every feature N1 shows differently, and on the
  BDDfy, LightBDD and NUnit4 lanes a name with an apostrophe between letters or a word of capitals before a comma (E16).
  Recommended (Q5): keep them, the #101 way. Each lane that titleizes a feature name records the name the id was computed
  from when it differs from the new display name, made with a frozen copy of 5.9.0's `Titleize`
  (`StringCasing.TitleizeAsOf590`, internal, never changed again, its comment saying why), never with the new one, which
  would give the owner's own `Month's` feature new ids. `Feature.StableIdName` (internal) holds it; the data file writes it
  as `stableIdName` only where it differs from `displayName`, with its writer, schema entry and `merge` reader made as
  `keyedByOutline`'s are (`ReportGenerator.cs:5410-5411`, `:7408`; `MergeableReportReader.cs:254`); and every
  `ScenarioStableId.For` caller (the 15 sites of section 2.4, `merge` included) hashes it in place of the display name,
  through one helper. The alternative is new ids, stated in the changelog with `kronikol history rename` (or
  `aliases.json`) as the way to carry history across.
- **N4. The outline heading (#102).** A Gherkin outline's grouped heading goes through the method-name humaniser
  (`ParameterGrouper.cs:53`), which lower-cases acronyms (`UK` to `uk`, #139) and rewrites a trailing `(...)` as `[...]`.
  It is the same defect class, a name not shown as written, on the same lanes. Recommended (Q6): fold it in, with a mark
  on the scenario that its name is shown as written (`Scenario.NameAsWritten`, internal; data key `nameAsWritten`,
  written only where true and read by `merge`, as `keyedByOutline` is), set by the Gherkin lanes (ReqNRoll and the
  Cucumber ingest), so `ParameterGrouper` takes such a scenario's outline id as written while xUnit and TUnit method names
  keep the humaniser.
- **N5. A plain Gherkin scenario ending in `(...)`** is grouped as a parameterised test with an `Arg 0` column (section 5,
  E8): the same mark keeps `ParameterParser` off a name shown as written, in an outline or not, and a merged report keeps
  it, since the data file carries the mark.

### 4.5 I5, the slider below whatever floats above it

- **S1. The free top.** `placeControls` (CM:1639-1647) moves the slider below the lowest bottom of what floats over the box:
  the report bar while it is pinned, and open settings stuck under it that come before the box in the page (in practice
  the box's own section's, the one settings that can be stuck over it). That is the rule of `freeTop(t, nav)`
  (`report-focus-script.js:30-41`), which today only keyboard focus uses: the bar counts once the mark that holds its place
  has passed the line it sticks at, a settings panel counts when it is sticky, laid out and at the band's top, and
  only one that comes before the target.
- **S2. One rule, one place.** That function moves into `report-tabs-script.js`, which every page carries and which already
  owns `--kron-nav-h` (`:75-90`), as `window.kronikol_freeTop(target)`: the same rule, returning the band's top with no
  gap. Each caller adds its own: keyboard focus its 12 px (`GAP`, `:12`), which `FocusClearOfTheBarTests` pins by name,
  and the slider S3's 11. Every fact of `FocusClearOfTheBarTests` goes through it, so the whole class runs after the move.
- **S3. The gap.** The slider's line is `free top + 11` where it is `bar + 8` today, so the painted thumb stands 4.5 px
  clear of whatever floats above it, 3 px more than its 1.5 px today (M-I5: the thumb is 14.5 px tall, its centre 6.9 px
  below its top); the same gap under the open settings. The constant gets a name and a comment saying it is the thumb's
  half-height plus 4.5 px.
- **S4. When to place it.** As today (hover, selection, scroll), plus when the settings open or close or change height
  (the settings script calls `followSoon` through a `window.` hook) and when the slider takes keyboard focus (a focused
  slider is placed only when it is also hovered or selected today, CM:1648-1655).
- **S5. Stacking.** Once placed below, the slider and the settings never overlap, so their z-indexes (10 and 4) stay.

### 4.6 I6, step bars like the step-param-table

- **B1. Centred.** `HorizontalAlignment center` in the `.stepBody` style (`PlantUmlCreator.cs:1258-1267`) centres every line
  of a styled bar and each of its tables as a block, on the pinned engine and on 1.2024.6 (section 2.6). A one-line bar is
  centred already.
- **B2. A doc string stays a block.** Centring alone centres each doc-string line on its own and loses a JSON body's
  indentation (M-I6, variant 1), so `Build` draws a doc string as a one-cell table whose lines break inside the cell
  (`\\n`): the block is centred, its lines keep their indentation, and it gets a frame, as the HTML `.step-docstring` has
  (M-I6, variant 4, on the pinned engine; Q7). The path keeps today's guards: each line is wrapped and escaped as a body
  line is today (`EscapeWrappedBodyLine`, `StepBarPlantUml.cs:154-155`), a `|` in it is written `<U+007C>` as a cell's is
  (`EscapeCell`, `:142-146`), and the pieces are joined with the cell's `\\n` after escaping, since `EscapeInline` rewrites every backslash.
  The cell is not cut by `TableCellBudget`, which shares a row's width between cells side by side, and the statement keeps
  `TruncateLabel`'s cap, so `SequenceDiagramWidthTests.A_step_bar_doc_string_line_still_fits` (389-395, a 900-character
  token) holds on 1.2024.6 as it does today. Its cell takes B3's room like any other.
- **B3. Room in a cell.** `TableRow` (`StepBarPlantUml.cs:125-135`) writes `<space:8>` where it writes one space each side
  today, and a zero-width strut above and below the text (`<sup><U+200B></sup>` before it, `<sub><U+200B></sub>` after it):
  measured, exactly the HTML's `padding: 3px 8px` (M-I6, variant 3). The cost is 6 px of height a row and two more
  `<text>` elements a cell; no fact or reader counts `<text>` in a cell, and the struts are zero-width spaces, never
  whitespace the blank-line rule could mistake for a spacer (`StepTableBarTests`' spacer check, section 2.6). Q8 asks
  whether the owner wants the vertical room at that cost.
- **B4. Header text centred, body text left, as the HTML.** The HTML centres `th` (the browser's default) and starts `td`
  at the left. The engine cannot centre a cell (`AtomTable.java:119-139`), so the role pass centres the header row's texts
  in their columns (it reads the columns from the vertical rules it already finds, RS:2458-2476), on the page and in its
  copies; Node, IKVM and Server keep the engine's left. (Q9: or centre every cell.) A header row is told by its text,
  which the engine draws bold for `|=` cells: the first row of a table whose texts are all bold. The rules alone cannot
  tell one (`headH` reads the horizontal rules, RS:2475), and a doc string's one-cell box (B2) has the same two rules as a
  header-only table; it has no bold row, so it gets no header shading, no centring and no weight, and its lines keep their
  x offsets.
- **B5. Closer still.** The role pass gives body cells `--kron-ink` and the header 600 weight, the HTML's; the cell font
  stays the bar's 13 px (Q9 asks about 12 px).
- **B6. One table, one ground.** The role pass clusters a bar's rules into tables before building grounds, so a bar with
  two tables gets two grounds and two shaded headers, and a header-only table (one bold row) gets its header shading
  (section 5, E10, E11); a doc string's box gets a ground and no header (B4).
- **Guards that move with B2 and B3:** `TruncateLabel` must never cut inside `<space:...>`, `<sup>`, `</sup>`, `<sub>` or
  `</sub>` (it refuses only `<U+`, `PlantUmlStatementLimits.cs:268-275`), nor between a cell's `\\n` and its `n`. The
  filter text's normaliser (`SearchNormalizer`) strips `<space:N>`, `<sup>`, `</sup>`, `<sub>` and `</sub>` from indexed text (it strips six tag
  names today, `Reports/SearchIndex/SearchNormalizer.cs:106-130`) and reads a cell's `\\n` as a break, where rule 5a
  (`SearchNormalizer.cs:43`, `report-search-index.js:71`) removes the `\n` and leaves the first backslash glued to the next
  line; `report-search-index.js:70-71` and the shared vectors move with it.

### 4.7 I7, full-width controls on a phone

- **P1. The report sheet.** In the phone block of `collapsible-notes-styles.css` (`:151`), the rule reads
  `.toolbar-right, .features-tools .toolbar-right { flex-direction: column; align-items: stretch; gap: 0 }`: at 0,2,0 in a
  sheet emitted after `stylesheets.css` it wins over the desktop row's `align-items: center` and `row-gap: 6px`
  (`stylesheets.css:805-808`), so every zone (Payloads, Format, Toggle) is as wide as the sheet, its selects with it, and the
  zones sit as close as the scenario sheet's. A page without tabs gets `.toolbar-row > .toolbar-right { align-self:
  stretch }` beside it.
- **P2. The Timeline's controls.** In the phone block: `.timeline-controls` takes the line (`flex: 1 1 100%; margin-left: 0`)
  as a column of full-width groups; each `.tl-seg` is a flex row with its label on a line of its own above its buttons
  (as `.details-radio-label` is, `collapsible-notes-styles.css:157`), and the buttons share the width equally
  (`flex: 1 1 0`). The Components tab uses the same classes (`ReportGenerator.cs:2948-2969`); recommended, the rule reaches
  both tabs, so they keep one look (Q10).
- **P3. The sweep sees it.** `ViewportSweepTests` opens the report's `.tools-gear` at 768 px and below, as it opens the
  scenario gears, so the report sheet is swept at every phone width (section 5, E12).

### 4.8 I8, the trend in one line, newest first

- **H1. One control for both cards.** Both titles say "oldest run first" and both cards are one trend, so one order
  control above the cards governs both (Q11 if the owner meant the pass rate alone).
- **H2. The rings scroll as the bars do.** The rings get `.history-rings-scroll` (`overflow-x: auto; overflow-y: hidden`), a
  flex row `width: max-content; min-width: 100%`, rings that grow to share a short row and never shrink, so a few runs still
  spread across the card and many scroll inside it. The card clips (`history-styles.css:81`), so the scroller is required.
- **H3. Markup and CSS only.** The labs page runs no script but the theme's, which three facts pin, and the cards are
  drawn "markup, not script" on purpose (`HistoryHtml.cs:249`). So the control is two native radios styled as the
  Timeline's segmented buttons (`tl-seg-btn`), "Order: Newest first | Oldest first", and `:has()` shows one of two lists
  per card: each list is written twice, newest first and oldest first. That keeps the reading order the shown order and
  gives each list an `aria-label` naming its own order; the titles become "Pass rate" and "Total duration", since the
  control says the order (a rename the ask did not make, so Q12 names it). The oldest-first copy is hidden unless its
  radio is checked, and only a `:has()` rule shows it, so an engine without `:has()` shows newest first alone, never both
  lists. Each list opens on the newest run: the newest-first scroller is left to right and starts at its
  left; the oldest-first one keeps today's right-to-left scroller and starts at its right (M-I8 measures both orders in
  Chromium, Firefox and WebKit). The cost is the second copy of each list (M-I8 measures it for 51 runs). Q12 has the
  alternatives: a script on the report with the labs page fixed at newest first, or a visual-only `row-reverse`.
- **H4. Newest first by default, in the markup** (the radio checked as written), so the labs page and a page with no script
  open newest first. No storage and no link entry, as the Timeline's segments have neither. The radios carry
  `autocomplete="off"`: Firefox otherwise restores a radio's checked state on reload, so "Oldest first" would survive a
  reload there although nothing is stored.
- **H5. The model keeps its order.** `HistoryVerdicts.Runs` stays oldest first, since `Runs[^1]` is the current run to five
  readers; only `HistoryHtml` writes the second order. The per-scenario sparklines and series strings stay oldest first,
  labelled so, shared as they are with `Failures.md`, CTRF and the CLI (Q13).

### 4.9 What changes for a reader

| Item | Before | After |
|---|---|---|
| I1 | a chip cycles on, red, off | on, off; a 2-WAY / 3-WAY switch beside ANY/ALL brings back on, red, off; an old link with a red chip opens its row three-way |
| I2 | a large diagram's notes and bars show yellow and black for a frame or more after a note's own redraw | the report's colours from the first frame |
| I3 | an empty loop box (and an empty Setup box) when its calls are hidden | the box goes with its calls and comes back with them |
| I4 | `Feature: Month's reports via gRPC` shows "Month S Reports Via G RPC" | as written |
| I5 | the slider's thumb 1.5 px under the bar, and over the open settings' top row | 4.5 px clear of the bar or of the open settings |
| I6 | bar text and tables start at the left, cells tight | centred, cells with the HTML table's room, header centred |
| I7 | the report's YAML and WRAP selects and the Timeline's buttons narrow on a phone | full width, as the scenario sheet's |
| I8 | the rings wrap onto rows, oldest first | one row that scrolls, newest first, with a switch to oldest first |
| I9 | the filter box's field says "Search...", and its title, help and readouts call its filtering a search | they say filter |

### 4.10 I9, the filter box says filter

The filter box's text field hides the scenarios that do not match it: it filters, as the rows beside it do, and nothing
on the page searches. Its words date from 4.x's search bar. A dedicated search is planned (`plans/SEARCH_PLAN.md`), and
the owner wants the two kept apart in words.

- **W1. What a reader sees says filter.** The field's placeholder becomes "Filter... (@tag, $status, dep:, duration:, &&,
  ||, !!)" (`ReportGenerator.cs:1926`); its title "Filter by names, steps, tags and diagram text. ..." (`SearchTitle`,
  `:3110`), the rest of the sentence as today; the `?` button's "How to write a filter: ..." (`SearchHelpTitle`, `:3113`);
  the help panel's rows and note ("Press / to focus the filter field", qualifiers that "narrow the filter", "the field
  is marked", `:1929-1944`); and every other string the page shows that calls the field's filtering a search: the
  readouts and live region, Clear All's title, the `/` key's description, the outline rows' highlighting, found at
  execution by a grep of the page's scripts and the generator for user-visible "search".
- **W2. No identifier changes.** `#searchbar`, `search_scenarios`, `toggle_search_help`, the `search-*` classes, the `q=`
  key, `advanced-search.js`, `SearchNormalizer`, the deep index (`#kron-search-index`) and the test helper `FillSearchBar`
  keep their names: a reader never sees them, a custom stylesheet or script and every saved link read them, and
  `SEARCH_PLAN.md` keeps `#searchbar` as its hidden field. Their comments say "filter" where they describe the field.
- **W3. The wiki.** The filter box's sections of `Generated-Reports.md` (`:98`, `:695`, `:705-731`, `:759`, `:965`, `:986`,
  `:1134`, `:1193`, `:1229`) and `Search-Syntax.md` say filter. The page keeps its file name, which the changelog and other
  pages link to, and its heading becomes "Filter Syntax", with a line saying it was "Search Syntax" up to 5.9.0. The
  changelog's past entries stay as written.
- **W4. Held by a test.** No text, placeholder, title or `aria-label` inside `.filtering-box` contains "search", in any
  case. SEARCH_PLAN's builder and Search tab, when they come, are a search and live outside the box.
- **Release:** R1. It corrects generated output and changes no identifier, option or member: a patch.

## 5. Bugs found on the way

CLAUDE.md asks for every bug found on the way to be fixed, red first. Each row below was found reading the code for
section 2; "shown" means a probe or the page's own functions run in node showed it, "read" means it is from the code
alone, and its red test comes first.

| # | Defect | Where | How known | Fix | With |
|---|---|---|---|---|---|
| E1 | The ANY/ALL buttons and a row's Clear are 28 px on a phone, under the 36 px every other filter control gets | `stylesheets.css:2202` lists the chips, not `.tag-mode-toggle`, `.dep-mode-toggle`, `.group-clear` | read | add them (and the new switch) to the list | I1 |
| E2 | The Status buttons and the Duration buttons carry their chosen state in a class only, no `aria-pressed` | `ReportGenerator.cs:1963`, `:1990`; `report-status-filter-function.js:1-4`; `report-duration-filter-function.js:24-26` | read | `aria-pressed` written and kept, as the chips do | I1 |
| E3 | A scenario skipped after a failure shows under the Status filter's Failed, while the summary and the ring count it as Skipped (5.0.0) and `$skipped` in the filter text misses it | `report-status-filter-function.js:22`; `ReportGenerator.cs:1779-1781`; `advanced-search.js:284-286` | read | the filter and the search count it as Skipped, as the summary does (Q15) | I1 |
| E4 | `ViewportSweepTests` opens the filter rows only at 768 px and below, so the Tags and Dependencies rows are never measured at a desktop width | `ViewportSweepTests.cs:230-253` | read | open More filters at every width | I1 |
| E5 | A participant whose alias is a block keyword (a service named Group, Loop, Alt, Opt, Par, Break, Critical or Partition) is read as a block opener: the splitter adds stray `end`s and repeats earlier calls in later parts, the Setup filter silently leaves the setup in, and the C# guard caps the message at the 600-character block-label limit | RS:966, `scanOpenBlocks` RS:1112; CN:3144, :3168, :3187; `PlantUmlStatementLimits.cs:419-424`, `:462-463` | shown (node, the page's own functions; `item3-loops.md`) | test for an arrow before an opener everywhere, as `parseTraceUnits` does (RS:854) | I3 |
| E6 | A C# split re-opens the Setup box eagerly, so when the next trace starts the action, the next diagram opens with an empty Setup box | `PlantUmlCreator.cs:2210-2214` with `:235-238`, `:266-272` | read | re-open lazily, before the next setup content, as the first opening does (`:286-287`) | I3 |
| E7 | The participant-row width estimate is never reset at a split, so after the first width split every later trace becomes a diagram of its own (about 159 diagrams where 3 would do, for 120 services, by estimate) | `PlantUmlCreator.cs:2009`, `:2022`, `:2037`; `FinishAndStartNewDiagram` clears the participants (`:2203`) but not the width | read | reset it with the participants | I3 |
| E8 | A plain Gherkin scenario whose name ends in `(...)` is drawn as a parameterised group headed by the text before the bracket, with an `Arg 0` column | `ParameterGrouper.cs:59-79`; `ParameterParser.cs:51-60`, `:87-90`, `:127-152` | read; M-I4 draws it | N5 | I4 |
| E9 | The Databases and Events filters read note body lines as statements, so a kept note line that looks like `x -> dbAlias: ...`, `activate dbAlias` or a `database "..." as` declaration is deleted (the Setup filter learned the note guard in 5.2.7) | `stripParticipantCalls`, CN:3056-3110 | read | skip note bodies, with `stripSetup`'s helpers | I3 |
| E10 | A step bar holding two tables gets one ground over both, the name line between them included, and only the first header is shaded | RS:2463-2476 | read | B6 | I6 |
| E11 | A header-only table in a step bar gets no header shading (`headH` is 0 with two horizontal rules) | RS:2475 | read | B6 | I6 |
| E12 | `ViewportSweepTests` never opens the report's `.tools-gear`, so the report sheet has never been swept at a phone width | `ViewportSweepTests.cs:247` | read | P3 | I7 |
| E13 | `scanOpenBlocks` skips note bodies but not `<style>` blocks, so a user's multi-line `group {` style line in a part with a forced boundary counts as an opener | RS:1105-1116 | read | skip style blocks as `stripSetup` does | I3 |
| E14 | Assertion tooltips pair the original source's assertion locations with the drawn assertion notes by index, so after the Setup filter removes earlier assertion notes the remaining tooltips name the wrong source line | `addAssertionTooltips`, CN:3298-3315 | read | read the locations from the source the picture was drawn from, as the parts already do | I3 |
| E15 | The outline and calibration drawings made aside for lined-up parts get a full role pass, main-thread time on every toggle for pictures nobody sees | RS:595-600, RS:2395, RS:1406 | read | the aside drawings are not sequence hosts | I2 |
| E16 | `Titleize` lower-cases an all-capitals word followed by a comma (`HTTP,` to `Http,`), as `value.All(char.IsUpper)` sees the comma | `StringCasing.cs:83` | read | test the letters only | I4 |
| E17 | The ReqNRoll wiki pages say the outline heading keeps the title as written, which is true of `OutlineId` and not of the heading (#102) | wiki `Integration-ReqNRoll-xUnit3.md:426`, `-xUnit2.md:442`, `-TUnit.md:424` | read | true once N4 ships; corrected with it | I4 |
| E18 | A focused slider is placed only while its box is also hovered or selected, so a slider reached by Tab keeps its last place | CM:1648-1655 | read | S4 | I5 |
| E19 | The 36 px touch size for filter controls applies only up to 480 px: the rules stand in a `max-width: 480px` block, while the wiki says "768 px wide or less" and 768 px has been the page's one phone breakpoint since 5.0.0, so from 481 to 768 px every filter control is 28 px | `stylesheets.css:2168`, `:2183`, `:2202`; wiki `Generated-Reports.md:697` | read | the touch rules move to the 768 px block (Q17: or the wiki says 480) | I1 |
| E20 | The wiki is wrong in two places these items touch: the API reference lists `StringCasing` members that do not exist (`Underscore`, `Dasherize`) and one that is internal (`Camelize`); the History pages say a window too long for its card scrolls and opens on the newest run, which only the bars do | wiki `API-Reference.md:71`; `Cross-Run-History.md:583-586`, `Generated-Reports.md:950-954` | read | corrected with I4 and I8, the changelog saying the wiki was wrong | I4, I8 |

Not changed, recorded: the in-page anchor slug drops non-ASCII letters (`ReportGenerator.cs:4902-4907`, `Café` gives
`caf`), but it is an id rather than text, and changing it would break links; the four hand copies of the `.stepBody` style
in the tests (section 2.6) become one `internal const` the tests read (test hygiene, no bump); two stale comments in
`StepBarPlantUmlTests.cs` (17-18, 21-22, 281) are corrected (no bump).

## 6. Tests

Every fact below is written first and run red on the release before it (a worktree at the previous tag with the new
test files copied in, as the earlier plans did), and its red message is read: a fact that fails for the wrong reason
proves nothing. "Red today" is 5.9.0, which changed nothing these facts read, so each fails there for the reason section 3
measured on 5.8.0. Playwright facts follow the CLAUDE.md rules (no forced clicks, no network mocking, `PollingInterval = 200`,
`dispatchEvent` for SVG, `.First`/`.Nth`). A fact that reads a diagram's source looks for statements at a line start
(`(?m)^loop `, `(?m)^partition\b`), since 5.8.0's look puts the words `group` and `partition` in every source. Each rule's
mutation (the rule broken by one edit) must turn at least one fact red; the list is in the harness (`mutations.md`).

### 6.1 I1

| Fact | File | Holds | Red today because |
|---|---|---|---|
| `A_chip_is_on_or_off_by_default` | E2E `FilterPipelineTests` | C2: two clicks give has then off, `aria-pressed` true then false, never `tag-exclude` | the second click leaves it out |
| `A_right_click_or_Alt_click_on_a_two_way_chip_is_a_plain_one` | same | C6: `contextmenu` not prevented and the chip unchanged; Alt+click turns the chip on, as a click does | it is prevented and the chip goes red; Alt+click leaves it out |
| `The_switch_makes_a_rows_chips_three_way_and_back` | same | C3, C5: on, the cycle is today's; off with a red chip, the chip is off, the scenarios show, the link loses `tagx` | no switch |
| `Each_row_has_its_own_ways` | same | C1: Tags three-way leaves Dependencies two-way | no switch |
| `A_link_with_a_left_out_chip_opens_its_row_three_way` (theory: `tagx`, `depx`, `hpx=1`, and a 5.0.0 link) | same | C4, C7: the switch shows 3-WAY, and the address keeps no `tagstates` | no switch |
| `The_link_carries_the_readers_own_switch_and_nothing_else` | same | C7: `tagstates=3` written only while the reader's switch is on, never for a red chip's three-way; a fresh page reads it back | no key |
| `Happy_paths_Hide_makes_the_tags_row_three_way_until_Show` | same | C4, Q1: Hide shows 3-WAY, Show brings 2-WAY back | no switch |
| `Turning_three_way_off_with_Hide_on_puts_Happy_paths_back_to_Show` | same | C5 | no switch |
| `Clear_All_makes_both_rows_two_way_and_a_rows_Clear_keeps_its_ways` | same | C8 | no switch |
| `A_chips_title_says_what_a_click_does_in_its_rows_ways` | same | C9: rewritten when the ways change, count kept | one text |
| `The_switch_works_on_the_Specifications_page_and_on_a_merged_report` | same | C12 | no switch |
| `A_switch_flip_is_one_step_back` | E2E `BackForwardFilterTests` | C7 | no switch |
| `Hide_then_Show_is_one_step_back_as_in_5_2_0` | same | C7: one entry, as today | (guard: green today, red if the switch's key were written for Hide) |
| `The_first_chip_change_after_an_old_link_is_one_step_back` | same | C7, from a `#tagx=` link | (guard, as above) |
| `The_filter_controls_are_touch_sized_on_a_phone` (390 and 600 px, each case setting its own viewport: the class opens at 375 by 812) | E2E `MobileResponsiveTests` | C11, E1, E19: every chip, ANY/ALL, switch and Clear at least 36 px tall | ANY/ALL 28 px at 390; everything 28 px at 600 |
| `The_status_and_duration_buttons_say_whether_they_are_chosen` | E2E `FilterPipelineTests` | E2 | no `aria-pressed` |
| `A_scenario_skipped_after_a_failure_is_filtered_as_skipped` | E2E `FilterPipelineTests` | E3 | it is under Failed |
| `The_rows_write_a_ways_switch_after_ANY_ALL` | unit `FilterRowsTests` | markup, titles, `data-scenarios`; the switch's accessible name is its visible text, with no `aria-label` and no `aria-pressed` (C9) | no switch |
| `No_token_leaks_into_a_generated_report` (extended) | unit `ToggleDefaultsMarkupTests` | the two new seeds | (extension) |
| `The_violet_theme_grounds_the_ways_switch_as_it_does_ANY_ALL` | unit `StylesheetRulesTests` | the violet hover edge, before the active states | no rule |

Moved, not lost: the facts that test three-way behaviour (`FilterPipelineTests` 81-100, 102-115, 133-147, 149-165, 167-185,
249-271, 273-306, 376-389; `TagFilterTests` 152-160; `BackForwardFilterTests.F3` 72-91) turn the switch on first, since
they hold the three-way cycle, and so do the two that put a chip into has not by a right-click
(`A_row_has_a_Clear_while_one_of_its_chips_is_set`, `Clear_All_clears_the_qualifiers_the_tri_state_chips_and_the_modes`,
section 2.1), whose Clear All fact also gains C8's check that both rows are two-way after it. The title pins
(`FilterRowsTests` 102-178, `ControlTitlesTests` 34 and 397-409, `TagsMarkupTests` 190-213) take the two texts.
`ViewportSweepTests` opens More filters at every width (E4), so the new control is swept at 320 to 1400 px.

### 6.2 I2

New E2E class `DiagramColourFrameTests`. Its instruments are F5's. For F1 and F2: the frame watcher of
`ReportToggleResponsivenessTests` (451-485), the next frame asked for inside the frame callback, every new picture shown
in the window checked for `data-k-roles`, note bodies computing `#FEFFDD` and bars computing black; and a
MutationObserver that fails at the end of any task leaving an untagged picture shown. For F3: the same checks made in a
ResizeObserver the test creates after the page's, observing every picture the page adds. Fixture: the generator's long
diagram (section 3, 40 calls, payloads of 24 to 400 lines, step bars with tables), drawn in parts. Each fact first
asserts the premise its case needs, so it cannot pass because the case never happened.

| Fact | Holds | Premise asserted first | Red today because |
|---|---|---|---|
| `A_notes_own_redraw_of_a_diagram_in_parts_never_shows_the_engines_colours` (light, dark) | F1 | the diagram is in two or more parts | the swap shows the parts untagged (M-I2) |
| `A_first_drawing_whose_later_part_lands_first_never_shows_the_engines_colours` | F2: part 0's answer held back by wrapping `window.plantuml.render` (the suite's own pattern, `ReportToggleResponsivenessTests.cs:375-382`; not network mocking) | `window.plantuml.maxParallel` is at least 2 (RS:667), so part 1 can land first; with one worker the queue waits for part 0 | `onQueueItemDone` decorates untagged |
| `The_wide_width_never_shows_the_engines_colours` | F1 through the refine pass | the refine redraw ran: the drawn width missed the box by more than 24 px (CN:1168) | the refine redraws by the note path |
| `A_diagram_redrawn_behind_another_view_is_coloured_in_the_frame_it_shows` | F3, the test's ResizeObserver | the picture was redrawn while its view was hidden | the IntersectionObserver reports after the paint |
| `A_far_scenario_reached_by_a_link_or_by_scrolling_never_shows_the_engines_colours` | F3: a report control redraws the diagrams of scenarios far below, which are skipped and box-less; a Timeline link jumps to one and a gradual scroll reaches another; the test's ResizeObserver and the frame watcher | the far pictures had no box when drawn | the IntersectionObserver tags them after their first frame in view |
| `The_report_controls_never_show_the_engines_colours` | the queue path, green today, kept so | | (control) |
| `The_aside_drawings_get_no_role_pass` | E15 | | they do |

Cost: `ReportToggleResponsivenessTests` gains the note path on its `Long` fixture: no long task over its budget (200 ms
times the measured contention stretch) from the double-click to the last part shown.

### 6.3 I3

E2E, the real emitter's source as `DiagramLoopLookTests.Source()` builds it, the database calls with category `SQL` (so
declared `database`) and broker calls with `MessageQueue`:

| Fact | Holds | Red today because |
|---|---|---|
| `Hiding_the_databases_takes_their_loop_with_them_and_showing_them_brings_it_back` | L1: no `(?m)^loop ` line left of a hidden call's loop, no loop box drawn (`rect[fill="#F4F7FB" i]`), both back after | the loop stays, empty (M-I3) |
| `Hiding_the_events_takes_their_loop_with_them` | L1 | the same |
| `A_Setup_box_left_empty_goes_and_one_still_holding_a_step_bar_stays` | L4 | the box stays empty |
| `An_emptied_Setup_box_holding_an_assertions_location_comment_goes` | L3: an assertion in the Setup box, Assertions and Steps hidden; the `'__^*__` comment keeps no block alive | the box stays |
| `A_user_box_emptied_by_the_databases_filter_goes` | L2, L4: `box` ... `end box` around database declarations | the box stays, empty |
| `Openers_and_ends_are_read_as_the_engine_reads_them` | L2: `Loop`, `GROUP`, `par2`, `& group` and `end group` emptied and dropped; `end box`, `end note` and `end ref` left to their own blocks | (new reading) |
| `A_destroy_or_a_return_keeps_its_block` | L3 | (guards L3) |
| `A_user_group_partly_emptied_stays_and_an_alt_with_one_live_branch_stays_whole` | L3, L4 | (kept today; guards L4) |
| `A_user_block_written_empty_is_left_as_written` | L1 | (kept today; guards L1) |
| `A_note_line_reading_end_and_a_style_block_with_group_are_not_blocks` | L2 | (guards L2) |
| `A_first_drawing_with_the_databases_hidden_has_no_empty_loop` | L5, the `ToggleDefaultsTests.cs:232` start | the loop stays |
| `The_current_source_copied_has_no_empty_loop` | L5 | it has |
| `A_participant_called_Group_is_a_message` (splitter and Setup filter) | E5 | stray `end`s; setup left in |
| `A_style_block_with_group_in_a_part_cut_at_a_forced_boundary_opens_no_block` | E13: `scanOpenBlocks` | the part re-opens a block |
| `A_note_line_naming_a_database_survives_the_databases_filter` | E9 | the line is deleted |
| `An_assertions_tooltip_names_its_own_line_after_the_setup_is_hidden` | E14 | the wrong line |

C# unit, `PlantUmlCreatorTests`: `A_split_never_writes_an_empty_Setup_box` (E6), `A_wide_test_splits_into_as_few_diagrams_as_its_width_needs`
(E7, a bound on the fragment count for 120 services), `A_message_to_a_participant_called_group_is_capped_as_a_message` (E5).

### 6.4 I4

| Fact | File | Holds | Red today because |
|---|---|---|---|
| `A_gherkin_feature_title_is_shown_as_written` (theory: `Month's reports`, `Month’s reports`, `Order Status via gRPC`, `Order Cross-Field Validation`, `Orders: retry on 503`, `Pancakes 🥞 flip`, `OrderProcessing`) | unit `ReqNRollScenarioInfoExtensionsTests`, `ReqNRollTUnitScenarioInfoExtensionsTests` | N1 | titleized |
| `Titleize_keeps_a_word_with_an_apostrophe_as_written` (theory, M-I4's values: `Month's birthday` "Month's Birthday", `Month’s birthday` "Month’s Birthday", `Don't bake twice` "Don't Bake Twice", `O'Brien's order` "O'Brien's Order", `the 'quoted' word` "The Quoted Word") | unit, a new `StringCasingTests` (no test calls `StringCasing` directly today; `Titleize` is reached through `FormatFeatureName`, `ScenarioTitleResolverTests.cs:387-412`, and the lanes' feature-name theories, section 2.4) | N2 | split, and "Month’S" through `ToTitleCase` |
| `Titleize_keeps_an_all_capitals_word_before_a_comma` | same | E16 | `Http,` |
| `TitleizeAsOf590_gives_5_9_0s_names` (M-I4's 5.9.0 column) | same | N3's frozen copy | (new function) |
| `A_feature_shown_as_written_keeps_its_scenarios_ids` (theory: ReqNRoll `Order Status via gRPC`, `Month's reports`, `Month’s reports`; a BDDfy story `Month's report`; LightBDD `Customer_s_Orders`; an NUnit4 fixture whose name holds an apostrophe; each through `merge` too) | unit, per lane | N3 (if Q5 keep): each id equal to the one 5.9.0 gives | (guard: green on 5.9.0; run red at the step with N1 and N2 and without N3) |
| `A_gherkin_outlines_heading_is_shown_as_written` (`A basket is weighed on the UK scale (<case>)`) | unit `ParameterGrouperTests` | N4 | `uk`, `[...]` |
| `A_gherkin_scenario_ending_in_brackets_is_not_a_parameterised_group` | unit `ParameterGrouperTests` | N5, E8 | grouped |
| `A_merged_report_keeps_a_name_shown_as_written` | unit `MergeableReportReaderTests` | N4, N5: `nameAsWritten` written, read, and the scenario still ungrouped after `merge` | (new key) |
| a lane fact: the ReqNRoll xUnit3 example gains `Feature: Month's specials via gRPC`, and the report's heading reads as written | the example's lane tests | N1 end to end | titleized |

The feature-name theories that pin titleizing on the ReqNRoll lanes (`ReqNRollScenarioInfoExtensionsTests.cs:420-430`,
`ReqNRollTUnitScenarioInfoExtensionsTests.cs:230-242`) turn round; BDDfy's and LightBDD's stay, gaining an apostrophe row
with N2's value.

### 6.5 I5

E2E `GradualZoomTests`, fixture `OpenTallWideAsync` (634-642), 1920 by 1080:

| Fact | Holds | Red today because |
|---|---|---|
| `The_slider_stands_clear_of_the_pinned_bar` | S3: the painted thumb's top at least 4 px under the bar's bottom (the probe's pixel scan, `i5-thumb.js`, as the fact's instrument) | 1.5 px (M-I5) |
| `With_the_settings_open_the_slider_stands_under_them_and_can_be_reached` | S1: under the settings by the same gap; `elementFromPoint` at its centre is the slider; the pointer walked up from the diagram keeps it shown | inside the settings' band (M-I5) |
| `Opening_the_settings_moves_a_shown_slider_below_them` | S4 | not re-placed |
| `A_slider_reached_by_Tab_follows_the_window` | S4, E18 | keeps its last place |

All of `FocusClearOfTheBarTests` must stay green through S2's move of `freeTop`: every fact in it goes through the function, not only 180-225.

### 6.6 I6

| Fact | File | Holds | Red today because |
|---|---|---|---|
| `A_diagram_with_a_styled_bar_gains_the_stepBody_style_block` (extended) | unit `StepBarPlantUmlTests` | B1 in the style | no alignment |
| the cell-byte facts (section 2.6's list) | unit `StepBarPlantUmlTests` and the four others | B3: `<space:8>` each side, the struts | one space |
| `A_doc_string_is_drawn_as_one_framed_cell` | unit `StepBarPlantUmlTests` | B2: one cell, each line wrapped and escaped as a body line, a `\|` as `<U+007C>`, joined by `\\n` after escaping, no cell budget | lines |
| `A_cut_never_lands_inside_a_space_sup_or_sub_tag_or_a_cells_break` | unit `PlantUmlStatementLengthTests` | the guards of B2 and B3 | (new tags) |
| `The_search_indexes_no_space_sup_or_sub_tag_and_reads_a_cells_break` | unit `SearchNormalizerEquivalenceTests` (its vectors are shared with the page's normaliser) | the guards of B2 and B3, a vector for `\\n` among them | (new tags) |
| `A_step_bar_doc_string_line_still_fits` (kept) | IKVM `SequenceDiagramWidthTests` (389-395) | B2 on 1.2024.6: a 900-character token still fits | (green today, kept so) |
| `A_bars_label_and_each_table_are_centred_in_the_bar` | E2E `StepTableBarTests` | B1: every line's and table's centre within 1 px of the bar's | left (M-I6) |
| `A_cell_has_the_html_tables_room` | same | B3 (and the struts if Q8) | about 3.5 px (M-I6) |
| `A_doc_strings_lines_keep_their_indentation` | same | B2 | (guard: green today; run red at the step with B1 and without B2) |
| `A_doc_strings_box_gets_no_header_and_keeps_its_lines_in_place` | E2E `DiagramRolesTests` | B4, B6 with B2: no `k-step-table-head`, no centring, every line's x as drawn | (the box is new) |
| `A_tables_header_is_centred_in_its_columns_and_its_body_starts_at_the_left` | E2E `StepTableBarTests` | B4, B5: the header told by its bold text | left |
| `Two_tables_get_two_grounds_and_a_header_only_table_is_shaded` | E2E `DiagramRolesTests` | B6, E10, E11 | one ground |

The paint facts of section 2.6 (`StepTableBarTests` 33-133, `DiagramRolesTests` 259-310, `DiagramExportLookTests` 80-170,
the E2E `CapturedTextEscapeTests` 164-178) stay green, re-pointed where they read a cell's left edge; the IKVM width fact
(`SequenceDiagramWidthTests.cs:356-395`) holds a 12-cell bar with B3's room under 4,096 px.

### 6.7 I7

E2E `MobileResponsiveTests` at 375 and at 600 px, each case setting its own viewport (the class opens at 375 by 812):

| Fact | Holds | Red today because |
|---|---|---|
| `The_report_sheets_zones_and_selects_are_as_wide_as_the_sheet` | P1, with and without tabs | the Format zone is its selects' width (M-I7) |
| `The_report_sheet_spaces_its_zones_as_a_scenarios_sheet_does` | P1 | 6 px apart |
| `The_timelines_control_groups_span_the_line_and_share_it_between_their_buttons` | P2 | groups at their content width (M-I7) |
| `The_components_controls_do_the_same` | P2, if Q10 | the same |

`ViewportSweepTests` opens the report's `.tools-gear` at 768 px and below (E12) and keeps its no-sideways-scroll rule.

### 6.8 I8

| Fact | File | Holds | Red today because |
|---|---|---|---|
| `Each_card_writes_its_runs_in_both_orders_newest_first_shown` | unit `HistoryRingsMarkupTests` | H3, H4: two lists per card, the newest-first one first, its radio checked and `autocomplete="off"`, each list's `aria-label` its order, titles without the order | one list |
| `The_oldest_first_lists_are_hidden_unless_a_has_rule_shows_them` | unit `StylesheetRulesTests` | H3: the hiding rule stands outside any `:has()`, the showing rule inside one | (new rules) |
| the order-reading facts (`HistoryRingsMarkupTests` 61-80, 87-119, 134-167) | same | read the oldest-first list by name | (re-pointed) |
| `A_window_of_fifty_runs_keeps_both_cards_on_one_line_and_opens_on_the_newest_run` (was the fact at 137-182) | E2E `HistoryRingsTests` | H2: the rings scroll in one row, the page does not; in both orders the newest run is in view at open | the rings wrap |
| `The_order_switch_turns_both_cards_round` | same | H1 | no switch |
| `A_reload_opens_newest_first_again` (Firefox) | same | H4 | no switch |
| `The_labs_page_orders_the_cards_with_none_of_the_reports_scripts` | E2E `HistoryRingsTests` (beside 184-218) | H3 on the labs page; its script pins (`LabsReportTests.cs:114-124`, `LabsPageTests.cs:79-87`) unchanged | no switch |
| the same two facts in Firefox and WebKit | E2E, an engine parameter as `BackForwardRestoreTests` has | `:has()` and the scroll start in each engine | no switch |

### 6.9 I9

| Fact | File | Holds | Red today because |
|---|---|---|---|
| `The_filter_box_never_calls_its_filtering_a_search` | unit `FilterRowsTests` | W1, W4: no text node, placeholder, `title` or `aria-label` inside `.filtering-box` contains "search" in any case, on a TestRunReport, a Specifications page and a page without tabs | "Search...", "Search names, ...", "How to write a search", the help note |
| `The_filter_fields_title_and_help_say_filter` | unit `ControlTitlesTests` | W1: the field's title and the `?` button's, word for word | the search titles |
| the placeholder and help-row pins (`FilterRowsTests.cs:57-62`, `:217-226`) and any E2E fact that reads the placeholder | unit, E2E | W1 | (re-pointed) |

## 7. Releases

### 7.1 Order and version

| Release | Holds | Bump, and why | Number |
|---|---|---|---|
| R1 | I2 to I7, I9 and every fix of section 5, E1 to E4 included (that table's "With" column names the item whose code and tests a fix shares, not its release); E1's line for the new switch comes with the switch in R2 | **patch**: every change is a fix or a correction of generated output; nothing new for a consumer to call. N3's recorded key (if Q5 keeps ids) is data-file plumbing on the #101 pattern, which shipped in patches (5.4.3, 5.4.4). | the patch after the newest release on `main` when it is pushed: 5.9.1 as things stand |
| R2 | I1 and I8 | **minor**, subject to Q16: each is a new report control (CLAUDE.md: "a new report control ... is a minor bump"). I1 also changes what a chip click does by default, and H4 the order an unchanged project's History cards open in. Both live in the page's script and markup, generated report output that CLAUDE.md's judgement call says is "not on its own a major bump", and neither touches an option, a type or a member. But CLAUDE.md files under MAJOR "changing a default so that existing code behaves differently without being touched", and the owner calls I1 "the default of the filters"; 5.4.0 shipped a changed default in a minor as the owner's own exception, which its changelog records as such. So the reading is the owner's to confirm (Q16). The changelog calls both changes out. | the minor after R1: 5.10.0 as things stand |

Q14 asks whether the owner would rather have one minor holding both. Numbers are claimed only when about to push
("taking X now" to the peers), never in advance.

Siblings: **5.9.0** (kronikol-2b, `plans/PLANTUML_EDITOR_TAB_PLAN.md`, released 2026-10-26 as `37428293`) changed
`context-menu-script.js` and `plantuml-browser-render-script.js`, which I2 and I5 also change; every `file:line` here was
moved onto it and each cited line checked to read the same at both commits. Its editor tab is one more reader of two fixes
here: "Open current PlantUML in editor" hands the tab the menu's current source (CM:962), which L1 has already cleared of
emptied blocks, and the tab cuts parts with the page's own splitter (`window._splitWithChunkedNotes`, RS:648-649), which E5
fixes.

**SEARCH_PLAN.md** (kronikol-41, on `main` as `1d03279b`, a docs-only commit; a minor: a dedicated search, the filter box hidden by default, the rows always
expanded, More filters gone) changes the filter box I1's switch sits in (`ReportGenerator.cs` about 1916-2030,
`report-filter-bar-script.js`, `report-filter-chips-script.js`); whichever of R2 and that release ships second rebases
onto the other. Where the two meet, as both plans record it (its section 7.4): its B6 is withdrawn (Clear All and Back
leaving open what a single-match search opened is 5.2.0's rule), so only C8 here changes `reset_filters`; its Search
tab's Status, Tags and Dependencies facets drive the filter box's own controls (its Q17), and a facet's Exclude sets a
chip red with `set_chip_state` and `filter_row`, as Happy paths' Hide does, so C4 makes the row three-way while the chip
is red and the facet never calls the reader's switch (`set_ways`), which alone writes `tagstates`; I9 rewords the field
its builder replaces (`ReportGenerator.cs:1926`, `:3110-3113`), whose ids it keeps; its address keys and `tagstates` / `depstates` collide with nothing; its
Filters count (F6) reads the four chip state classes, which I1 neither adds to nor renames; its builder reads
`TagChips.Count` inside the rows I1 rewrites (`ReportGenerator.cs:2008`, `:2022`) and reads `data-scenarios` if R2 ships
first; its F5 shows every row whenever the box is open and gives `ViewportSweepTests` a "box open" state, which is E4's fix
if it ships first; the stylesheet lines do not overlap.

### 7.2 Where

Each release in a worktree of its own made from `origin/main` with
`git -c core.autocrlf=false -c core.eol=lf worktree add`, never the shared checkout; a full Playwright suite only after
the peers' "done", announced with "starting Playwright" and "Playwright done"; the wiki edited in a worktree of the wiki
of its own, applied after the release is published.

### 7.3 Documentation

| Item | Wiki (`../Kronikol.wiki`, lines as of 2026-10-26, re-read from a fresh fetch at execution) | Elsewhere |
|---|---|---|
| I1 | `Generated-Reports.md:741-768` (Happy paths, Tags and Dependencies filters, the link), `:1186-1197`; `Tags-and-Attributes.md:20-22`, `:65`, `:70`; `Migrating-to-v5-Report.md:22`, `:25`, `:79-80`, `:116-121`; `Migrating-to-v5-Tags.md:14`, `:18`, `:51`; `Report-Themes.md:12-22`; `Report-Configuration.md:280-293` (the link grammar, what Clear All resets); `Generated-Reports.md:697` (a filter control 36 px tall "768 px wide or less", which the CSS holds only to 480 px: E19, whichever way Q17 goes) | the XML docs of `FilterChip`'s comments, `ReportGenerator.cs:1949-1953`, `:3021-3026` |
| I2 | `PlantUML-Browser-Rendering.md` (the role pass's paragraph) | |
| I3 | `Generated-Reports.md:884-898` (Databases, Events and Setup); `PlantUML-Browser-Rendering.md:140-143`; `Report-Configuration.md:116` (`CollapseConsecutiveIdenticalCalls`) | the option's XML doc (`ReportConfigurationOptions.cs:576-585`) says what hiding does to the loop |
| I4 | `Integration-ReqNRoll-xUnit3.md:426`, `-xUnit2.md:442`, `-TUnit.md:424` (E17); `API-Reference.md:33`, `:71` (`Titleize`, `FormatFeatureName`; `:71` also lists `Underscore` and `Dasherize`, which do not exist, and the internal `Camelize`: E20); `Cross-Run-History.md` (ids, whichever way Q5 goes) | the changelog: a renamed feature can move in the page's order and in `sN` addresses (N1) |
| I5 | `PlantUML-Browser-Rendering.md:173` (the slider follows "below the report's bar") | |
| I6 | the step bars' paragraph of `Generated-Reports.md` (found by grepping `stepBody` at execution) | |
| I7 | `Generated-Reports.md:692-698` (already says what P1 makes true) | |
| I9 | `Generated-Reports.md` (`:98`, `:695`, `:705-731`, `:759`, `:965`, `:986`, `:1134`, `:1193`, `:1229`); `Search-Syntax.md` (heading "Filter Syntax", file name kept, W3); `Home.md` and `_Sidebar.md` where they name the page | the generator's and scripts' comments that describe the field (W2) |
| I8 | `Cross-Run-History.md:583-586`; `Generated-Reports.md:950-954` (and `:144` if the labs page changes); both say today that a window too long for its card scrolls and opens on the newest run, true of the bars alone (E20) | `HistoryHtml.cs` doc comments (244-250, 271-278) |

Each release: `CHANGELOG.md` (which part of the version moved and why), every package's version, and a Kronikol4J
divergence-ledger line (`C:/Code/Kronikol4J/docs/REMAINING_PARITY.md`): page bytes for I1, I2, I3, I5, I6, I7, I8 ("not
mirrored": the port's page is 3.0.43's, with no role pass, no hide filters and no history, and for I3 the port writes no
loops at all, having no call collapsing), `titleize` for N2 and N3's frozen copy (ported, or "diverges" with the reason;
the port computes stable ids, so a kept id needs the same key there), and the status line N2 can change
(`PlantUmlCreator.cs:1172`).

### 7.4 Acceptance

- The harness's probes (section 3, Appendix A) run against each release's build, the generator pointed at it by
  `#:project <release worktree>/src/Kronikol/Kronikol.csproj` in place of `#:package Kronikol@5.8.0`, each at its rule's
  threshold: no raw frame for any trigger in either theme (M-I2; for F3's paths read on the screencast, which shows what
  was painted, since the probe's frame watcher samples before the page's ResizeObserver runs, F5); no empty block in any
  toggle state (M-I3); the slider's gaps and reach (M-I5); every line and table centred within 1 px and the cells' room
  (M-I6); every zone, select and segment as wide as its sheet at 375 and 600 px (M-I7); one row, the newest run in view in
  both orders, in Chromium, Firefox and WebKit (M-I8); the switch on the real rows (M-I1).
- BreakfastProvider on local packages, nothing configured: its five titleized features read as written, and (with Q5's
  keep) their scenarios keep their ids and their history in the History tab, compared by id, not by position, since N1
  can move a feature in the order.
- Before and after pictures, light and dark, for the owner: the screencast frames of I2, and I3, I5, I6, I7, I8, I1.
- The full core, Playwright and IKVM suites, and `release.slnf` built in Release for every target framework, before each
  tag.

## 8. Questions for the owner

| # | Item | Question | Recommended |
|---|---|---|---|
| Q1 | I1 | Happy paths' **Hide** is the Happy Path chip's red state. On a two-way Tags row, should it (a) make the row three-way while it lasts, Show making it two-way again, (b) be offered only when the row is three-way, or (c) stay as it is, a red chip on a two-way row? | (a): one rule, a red chip always means three-way (C4), and the address and Back keep 5.2.0's entries (C7) |
| Q2 | I1 | On a two-way row, should a right-click and Alt+click be (a) a plain click and the browser's own menu, or (b) a shortcut that leaves the chip out and turns the row three-way? | (a): two-way is the simple default the ask wants |
| Q3 | I1 | The switch's look: the two or three lines, the pill of states, the words 2-WAY / 3-WAY, or the state glyphs (the pictures of M-I1)? | the words: the lines read as `=` and `≡`, the pill is too small, the glyphs are busier |
| Q4 | I1 | Should a configured start state exist (`ReportToggleDefaults.TagChipWays` / `DependencyChipWays`, a minor's new surface)? | no, until someone asks: the seed is ready for it (C13) |
| Q5 | I4 | When a feature's display name changes (a Gherkin heading shown as written, N1; a name with an apostrophe on the BDDfy, LightBDD or NUnit4 lanes, N2), should its scenarios **keep their ids** (a key recorded with a frozen copy of 5.9.0's `Titleize`, on the #101 pattern, on every such lane) or take new ones (the changelog naming `kronikol history rename`)? Also: a Gherkin title written `OrderProcessing` or `my_feature_name` then shows so, as on the Cucumber lane. | keep the ids on every lane; and yes, as written |
| Q6 | I4 | Fold in #102 (the outline heading's lower-cased acronyms and `[...]`) and E8 (a Gherkin name ending in brackets grouped as parameterised)? | yes: the same class of defect, on the same lanes |
| Q7 | I6 | A doc string in a centred bar: (a) a framed block whose lines keep their indentation, or (b) each line centred? | (a) |
| Q8 | I6 | Vertical room in a cell needs zero-width struts in every cell: measured, exactly the HTML's 3 px above and below, at 6 px of height a row and two more `<text>` a cell (M-I6). Take it, or pad sideways only? | take it: it is what "more internal padding" asks |
| Q9 | I6 | Cells: (a) the header centred and the body at the left, as the HTML table, or (b) every cell centred? And the cells at the HTML's 12 px, or the bar's 13 px? | (a); 13 px |
| Q10 | I7 | Should the Components tab's controls, which share the Timeline's classes, take the same phone rule? | yes, one look |
| Q11 | I8 | One order switch for both cards, or for the pass rate alone? | both: one trend, and both titles say "oldest run first" |
| Q12 | I8 | The switch as markup and CSS with each list written twice (the labs page runs no script), or a script on the report with the labs page fixed at newest first, or a visual-only reverse? And the titles then read "Pass rate" and "Total duration", since the switch says the order: a rename the ask did not make. | the first (M-I8 has its cost); and the rename |
| Q13 | I8 | The per-scenario sparklines and series strings: keep them oldest first (shared with `Failures.md`, CTRF and the CLI, and labelled so)? | yes |
| Q14 | all | Two releases (a patch of fixes, then a minor with the two controls), or one minor? | two: the fixes need not wait |
| Q15 | E3 | Should the Status filter and the filter text count a scenario skipped after a failure as Skipped, as the summary does since 5.0.0? | yes |
| Q16 | I1, I8 | I1's two-way chips and H4's newest-first cards change what an unchanged project's report does. Ship them in a minor, as generated report output (CLAUDE.md's judgement call), or hold them for v6, since CLAUDE.md files a changed default under MAJOR and asks first? | a minor: both are the page's script and markup, no option, type or member changes, and the old ways stay one click away (the switch, the order radios) |
| Q17 | E19 | The 36 px touch size for filter controls holds to 480 px in the CSS and to 768 px in the wiki. Which is meant? | 768 px: it is the page's one phone breakpoint, and a phone held sideways is a touch screen too |

## 9. Log

**2026-10-26, green-lit.** The owner: "Implement the plan in full, make sure to not interfere with any other sessions",
which takes the recommended answer of every question in section 8 (Q1 (a), Q2 (a), Q3 the words, Q4 no option, Q5 keep
the ids, Q6 yes, Q7 (a), Q8 the struts, Q9 (a) at 13 px, Q10 yes, Q11 both cards, Q12 markup and CSS with the rename, Q13
yes, Q14 two releases, Q15 yes, Q16 a minor, Q17 768 px), and added I9 (section 4.10). SEARCH_PLAN's revision of the
same day withdrew its B6 and made its facets drive the chips (section 7.1).

**2026-10-26, the draft reviewed.** An independent review read the draft against the code at 5.9.0 (about 150 citations
re-opened, five wrong) and raised 25 findings, each checked again by hand before it changed anything here. 24 held; the
one that did not (the port's page has no hide filters at all, not "the Databases and Events filter") still changed the
ledger's reason. What changed:

- **F3** colours a waiting picture in the frame it gets its box, wherever it is, bounded by the scenarios'
  `content-visibility`; a version written during the review, which left far pictures to an IntersectionObserver with a
  margin, would still have painted a long jump raw. **F5** says which
  instrument sees which rule, since a frame watcher sampling in `requestAnimationFrame` runs before the page's
  ResizeObserver and would count frames that are never painted raw.
- **N2** measured (`gen/titleize.cs`): `ToTitleCase` reads U+2019 as a word break, so a word with an apostrophe never
  goes through it. **N3** keys the ids with a frozen copy of 5.9.0's `Titleize` (the new one would have given the
  owner's own `Month's` feature new ids) on every lane that titleizes, BDDfy, LightBDD and NUnit4 included (Q5).
  **N4/N5** name their mark (`nameAsWritten`) and carry it through the data file and `merge`.
- **B2** gives the doc string's emitter path; **B4/B6** tell a header row by its bold text, so the doc string's
  one-cell box is never shaded as a header; the guards cover `<sup>`, `<sub>` and the cell's `\\n`.
- **C7** writes `tagstates` for the reader's own switch only, red chips implying three-way, so Back and Forward keep
  5.2.0's entries; **C9** makes the switch's visible text its name, no `aria-pressed` (WCAG 2.5.3 and the ARIA button
  pattern).
- **L2-L4** read block keywords as the engine does (any case, `&`, `end` with text), add `destroy`, `return` and `||N||`,
  and drop a `box` emptied of its declarations.
- **S1/S2** give the shared free-top function its real shape (the target, no gap).
- **H3/H4**: no `:has()` shows newest first alone; `autocomplete="off"` keeps a reload newest first in Firefox.
- **R2**'s reasoning no longer cites 5.4.0 as support: Q16 asks. E19 (the 480 px touch block, Q17) and E20 (two wiki
  pages already wrong) are new; missed pins and facts were added to sections 2.1 and 6; four harness defects were fixed
  (the phone probe's line count, the flash probe's silent frame cap, a usage line, Appendix A's shell syntax).

## Appendix A. Harness

`plans/REPORT_UI_FIXES_PLAN.harness/`, run from that folder in Git Bash; `$OUT` is any absolute folder outside the
repository.

| File | What it does |
|---|---|
| `gen/gen.cs` | the probe pages from Kronikol 5.8.0's emitter and generator: `dotnet run --file gen/gen.cs -- "$OUT"` (the published package, `#:package Kronikol@5.8.0`; assembly named `formatter-probe` for the internals). For a release's acceptance (7.4), the `#:package` line becomes `#:project <release worktree>/src/Kronikol/Kronikol.csproj`, so the pages come from that build |
| `gen/titleize.cs` | M-I4's second table: 5.9.0's `Titleize` against N2's, a copy of each, on the names of sections 2.4 and 4.4: `dotnet run --file gen/titleize.cs` |
| `probe/common.js` | opening a page, frame waits, opening a scenario until its diagram is drawn and tagged |
| `probe/i1-switch.js` | M-I1: the four switch candidates in the real filter rows, pictures at 1280 (light, dark) and 390 px |
| `probe/i2-flash.js` | M-I2: `<main.html> <light|dark> <note|payloads> "$OUT"`, the frame watcher, the task check and a screencast; it fails when its 600-frame watch ends while the diagram is still drawing. Its frame watcher samples before the page's ResizeObserver runs, so for F3's paths the screencast is the reading (section 4.2, F5) |
| `probe/i3-loops.js` | M-I3: every toggle state's empty blocks and drawn loop boxes, pictures |
| `probe/i4-group.js` | M-I4 on the page: the feature heading and the bracketed scenario's grouping |
| `probe/i5-slider.js`, `probe/i5-thumb.js` | M-I5: the slider's box against the bar and the open settings, the pointer's walk; the painted thumb by pixel scan |
| `probe/i6-stepbars.js` | M-I6: `<stepbars.html> "$OUT" [light|dark]`, centring and cell room per bar per variant, pictures |
| `probe/i7-phone.js` | M-I7: `<main.html> "$OUT" <width>`, the two sheets' zones and selects, the Timeline's groups and how many lines each button's label takes |
| `probe/i8-history.js` | M-I8: `<history-N.html> <today|proto> <width> "$OUT" [chromium|firefox|webkit]`; `proto` injects section 4.8's design |
| `mutations.md` | one breaking edit per rule, for section 6's proofs |

The probes need `PLAYWRIGHT_CORE` pointing at the `playwright-core` the .NET package ships, which matches the browsers
the E2E suite installs:

```bash
PLAYWRIGHT_CORE="$USERPROFILE/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package" node probe/<probe>.js ...
```

A newer `playwright-core`, such as Kronikol4J's 1.61, looks for browser builds that are not installed. The probes launch
only Playwright's own browsers, never an installed Chrome or Edge (CLAUDE.md), through
`tools/playwright/installed-browser.js`.
