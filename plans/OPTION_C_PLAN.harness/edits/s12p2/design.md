# S12 phone layout, part 2 (draft; apply AFTER D49, which titles the same toggle at RG 1817)

Base read at db663d88 (RG = src/Kronikol/Reports/ReportGenerator.cs, SS = stylesheets.css).

## What is there
- RG 1815-1817: `.filtering-box-header` holds `h2` Filtering and `.filtering-box-export` with `span.filter-readout
  role=status` + Clear All + exports; then `.filter-search` (search field, help), then `<div class="mobile-filter-toggle">
  Filters</div>` (a div: not keyboard reachable, no role), then `#filter-rows.filters`.
- report-init-script.js 19-31: on a phone at load (`matchMedia('(max-width: 768px)').matches`, read once) the rows get
  an inline `display: none` and the toggle flips `filter-open` and the inline display. A second fold model beside S7's
  `rows-folded` class (the brief's risk).
- SS 1980-1998: `.mobile-filter-toggle` hidden on desktop, `color: rgb(100, 100, 100)` (literal, wrong in dark), a `▸`
  glyph in ::before rotated when open. SS 2036-2043 (phone block): `.filter-search` sticky, top 0, z-index 100,
  `background: rgb(224, 224, 224)` (literal, wrong in dark), padding 0.4em 0. SS 192-193: an empty
  `@media (min-width: 768.02px) { }` block.
- The readout's one writer: report-scenario-feature-map-helper.js 72 (`querySelector('.filter-readout')`): MOVE the
  element, never copy it.

## The change
1. Markup: `<div class="mobile-filter-line"><button type="button" class="mobile-filter-toggle" aria-expanded="false"
   aria-controls="filter-rows">Filters</button></div>` in place of the div (keep the class: ViewportSweepTests,
   FilterBarTests, ToolbarStylingTests click `.mobile-filter-toggle`). D49's title goes on the button.
2. Script (report-init-script.js): the toggle sets `aria-expanded` with `filter-open`; the readout moves into
   `.mobile-filter-line` while the phone query matches and back to `.filtering-box-export` (before Clear All) when it
   stops matching (a `change` listener, as the preview's c-dom.r5.js 390-404 does). Keep the load-time fold, but key it
   off the same listener so a rotate across the breakpoint does not leave rows hidden on a desktop width (check
   whether S7's rows-folded already covers the desktop side; if so, clear the inline display when leaving the phone).
3. CSS: `.mobile-filter-line { display: none }`; phone: flex, space-between, align center; the toggle a button reset
   (background none, border 0, font inherit, padding 0.4em 0) in `var(--kron-ink-soft)`; the readout on that line in
   the readout's own style. Phone `.filter-search`: keep sticky (MobileResponsiveTests 369-377 pins it) with
   `background: var(--kron-bg)` (check the token name for the page ground) in place of the grey, so it reads in dark.
   Check the preview's c-extra.css 39 and 48 and c-round5.css 42-45 for the exact look before writing it.
4. Remove SS 192-193 (empty block).
5. ViewportSweepTests 52-54: the 1160 px constant's comment says stylesheets.css states it in the band block; no sheet
   has had that band since S2 and the measure script takes the value without using it. Say what 1161 is now (a width the
   4.x header row broke at) or drop it with the unused parameter.

## Tests (first)
- core: the toggle is a button with aria-expanded and aria-controls, inside `.mobile-filter-line`; no literal
  rgb(100, 100, 100) or rgb(224, 224, 224) in the phone rules (CssRules.Value); the empty block gone.
- Playwright at 400 px: the readout sits on the Filters line and still updates (search narrows it); at 1280 px it is
  back in the box; Tab reaches the toggle and Enter opens the rows (aria-expanded true); dark theme: the sticky search
  line's background equals the page ground token; a resize from 400 to 1280 leaves the rows shown.
- Pins that move: core FilterRowsTests 148, 154 (the readout's place); MobileResponsiveTests sticky pin (should hold).

## The preview's exact look (read 21:50)
- c-extra.css 39: `.filter-search { background: none; padding: 0; border-radius: 0; }` at every width (the preview's
  search line had no sticky ground; the product keeps sticky on a phone, so give it the filtering box's own ground
  token rather than none, and check what the box paints).
- c-extra.css 48: `.mobile-filter-toggle { font: 600 11px var(--kron-font); letter-spacing: 0.06em; text-transform:
  uppercase; color: var(--kron-label); }` (the filter rows' label look).
- c-round5.css 42-45: `.mobile-filter-line { display: flex; align-items: center; justify-content: space-between; gap:
  12px; }`, `> .mobile-filter-toggle { flex: none; }`, `> .filter-readout { min-width: 0; margin: 0; padding: 0;
  border: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }`.
- c-dom.r5.js 390-404: wraps the toggle in `div.mobile-filter-line`; `placeReadout` moves the readout onto that line
  while `(max-width: 768px)` matches and the toggle is displayed, else back before Clear All; runs at load and on the
  query's `change`. The product emits the line in RG instead of building it in script.

## DECISIONS (2026-10-19 23:45, read on s11a = 3fce16bb + D48 + count fix)
- ONE fold model at every width: the phone's Filters toggle toggles the same `rows-folded` class More filters does.
  Move SS 293-296 (`.filtering-box.rows-folded:not(.help-open) > .filters {display:none}` and the help-open rule) out
  of `@media (min-width: 768.02px)` to top level. Delete report-init-script.js 19-31 (inline display fold) and reword
  its parse_url_hash comment (drop "the mobile blocks above folded"). Handler: filter-bar script 291-297's delegated
  click takes `.filtering-box .mobile-filter-toggle` too; hooks.paint sets the toggle's aria-expanded (= !rows-folded).
  ::before rotates on `[aria-expanded="true"]` (drop `filter-open`; no test reads it).
- BUGS this fixes (red facts): (1) phone: "?" shows nothing while the rows are folded (panel is inside .filters,
  inline none); (2) loaded at 400 then widened: More filters cannot open the rows (inline none stays); (3) loaded at
  1280 then narrowed: the toggle has no listener (attached only if phone at load) and the rows show; (4) flash of open
  rows before DOMContentLoaded on a phone (CSS folds from first paint now).
- Readout: placeReadout() in the filter bar script (start + phone change, addListener fallback): phone -> append to
  `.mobile-filter-line`; else insert before Clear All. Box-relative `$('.filter-readout', box)` still finds it.
- CSS: `.mobile-filter-line {display:none}`; phone `{display:flex; align-items:center; justify-content:space-between;
  gap:12px}`; toggle = button reset (background none, border 0, padding .4em 0, cursor pointer, inline-flex, gap .5em)
  + `font-size: var(--kron-fs-label); font-weight: 600; letter-spacing: var(--kron-label-track); text-transform:
  uppercase; color: var(--kron-label)` + focus-visible accent outline; `.mobile-filter-line > .filter-readout {min-width:0;
  margin:0; padding:0; border:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap}`; phone `.filter-search`
  background `var(--kron-card)` (the box's ground); remove the empty block SS 194-195 (+ blank run 189-193).
- Pins that move: MobileResponsiveTests 329-367 read `f.style.display` -> read getComputedStyle display.
- Tests: core (new MobileFilterLineTests or in FilterBarTests): button markup + order; fold rule at top level; toggle
  ink a token + AA in 3 blocks vs --kron-card (reuse Contrast from the count fact); phone search ground a token; no
  empty @media block in any sheet. Playwright: readout on the line at 400 + updates; back before Clear All at 1280;
  resize both ways; Tab from "?" reaches the toggle, Enter opens (aria-expanded); one state across a resize both ways;
  bugs (1)-(3); dark: sticky search ground == box ground.

## WRITTEN 00:35: tests-s12p2-core.py (core). TO WRITE: scripts-s12p2.py + tests-s12p2-e2e.py
- RG: D49's `<div class="mobile-filter-toggle" title="Show or hide the filters">Filters</div>` ->
  `<div class="mobile-filter-line"><button type="button" class="mobile-filter-toggle" aria-expanded="false"
  aria-controls="filter-rows" title="Show or hide the filters">Filters</button></div>`; extend the readout comment.
- init script: delete the "#2 Collapsible filter section on mobile" block; drop the "Run first, it was aimed before the
  mobile blocks above folded..." sentence of the parse_url_hash comment.
- filter bar script: delegated click closest('.filtering-box .more-filters, .filtering-box .mobile-filter-toggle');
  paint sets the toggle's aria-expanded = open; placeReadout() in start() before bar() + phone change listener
  (addListener fallback): phone -> line.appendChild(readout); else insert before
  `.filtering-box-export .export-btn[onclick="clear_all_filters()"]`.
- CSS: fold rules (SS 293-296) out of the min-width block; toggle block 1981-1998 -> line hidden + button reset +
  label look + focus-visible + ::before on [aria-expanded="true"] + `.mobile-filter-line > .filter-readout`; phone block
  `.filter-search` background var(--kron-card); phone `.mobile-filter-toggle { display: flex }` -> `.mobile-filter-line
  { display: flex; align-items: center; justify-content: space-between; gap: 12px }`; empty block 194-195 + blanks.
- Playwright class MobileFilterLineTests (8): readout on the line at 400 + counts after a search; before Clear All at
  1280 and moves with the width both ways; Tab from "?" reaches the toggle, Enter opens (aria-expanded), Space folds;
  "?" opens the help on a phone with the rows folded (RED, bug 1); opened on a phone stays open at 1280 + More filters
  expanded, folded at 1280 stays folded at 400 (RED, bug 2); opened wide then narrowed: rows hidden, Filters opens them
  (RED, bug 3); opened narrow then widened: More filters opens them (RED, bug 2b); dark: sticky search ground == box
  ground (RED). Pins that move: MobileResponsiveTests 329-367 style.display -> getComputedStyle; ViewportSweepTests
  52-54 comment.
