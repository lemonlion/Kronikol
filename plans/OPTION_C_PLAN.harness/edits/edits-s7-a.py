# S7: More filters and the folded rows (D15), the deep search chip inside the field (D17), the measured layout's classes
# (D18), the bar carrying the filters (D24) and pinned (D25). The preview's c-skin.css 582-591, 1148-1160, 1331-1410,
# 1595-1597 and 1743-1746 (c-round8.css, c-round9.css and the later rounds as the skin composes them), in its final
# state. One change from the preview: the box riding in the bar draws its rule on its bottom edge, between it and the
# tabs, since round 17 put it at the top of the bar (round 8's rule stayed on its top edge, along the bar's own).
SEARCH = r'''
/* More filters (5.0.0, S7, D15): a control the height of the search field, at the end of its line, selected while the
   rows are open; while they are folded it counts the filters on in them. On a desktop the rows start folded, and the
   search help opens on its own; on a phone the box's own Filters toggle folds them. */
.search-field { position: relative; display: flex; flex: 1 1 auto; min-width: 0; }
.more-filters {
    flex: none; align-self: stretch; display: inline-flex; align-items: center; gap: 6px; padding: 0 12px;
    border: 1px solid var(--kron-select-border); border-radius: 8px; background: var(--kron-card); color: var(--kron-ink-soft);
    font: 400 var(--kron-fs-control)/16px var(--kron-font); white-space: nowrap; cursor: pointer;
}
.more-filters:hover { background: var(--kron-hover); }
.more-filters[aria-expanded="true"] { background: var(--kron-selected); border-color: var(--kron-selected-edge); color: var(--kron-selected-ink); }
.more-filters.has-on { border-color: var(--kron-selected-edge); color: var(--kron-selected-ink); }
.more-filters:focus-visible { outline: 2px solid var(--kron-accent); outline-offset: 1px; }
.more-filters svg { transition: transform .15s ease; }
.more-filters[aria-expanded="true"] svg { transform: rotate(180deg); }
.more-filters-count {
    display: inline-block; min-width: 18px; padding: 0 5px; box-sizing: border-box; border-radius: 999px;
    background: var(--kron-accent); color: var(--kron-on-solid); font-size: var(--kron-fs-label); font-weight: 600;
    line-height: 18px; text-align: center; font-variant-numeric: tabular-nums;
}
.more-filters-count[hidden] { display: none; }
@media (min-width: 768.02px) {
    .filtering-box.rows-folded:not(.help-open) > .filters { display: none; }
    .filtering-box.rows-folded.help-open > .filters > :not(.search-help-panel) { display: none; }
}
@media (max-width: 768px) { .more-filters { display: none; } }
@media (prefers-reduced-motion: reduce) { .more-filters svg { transition: none; } }
/* the deep search's chip sits inside the field, from its own right edge (D17) */
.search-field .kron-deep-chip { right: 8px; }

/* Duration beside Status when both runs fit whole, and Happy paths beside them when all three do, as
   report-filter-bar-script.js measures them (D18); otherwise each keeps a line of its own */
.filter-row.pair-fits { column-gap: 28px; }
.filter-row.pair-fits > .duration-filters, .filter-row.trio-fits > .happy-filters { flex-basis: auto; }
.filter-row.pair-fits .duration-filters-label, .filter-row.trio-fits .happy-filters-label { flex-basis: auto; }
'''

BAR = r'''
/* The report bar carries the filters (5.0.0, S7, D24): right of the tabs on their row, what the filters leave showing,
   Clear All while anything is filtered, and Filters, which takes the box into the bar while the bar floats. */
.nav-row { display: flex; align-items: center; gap: 12px; padding-right: 12px; }
.nav-row > .report-tabs { flex: 1 1 auto; min-width: 0; }
.report-nav.on-features .nav-row { border-bottom: 1px solid var(--kron-hairline-soft); }
.nav-filters { flex: none; display: flex; align-items: center; gap: 10px; }
.nav-readout { font-size: var(--kron-fs-meta); line-height: 20px; color: var(--kron-muted); font-variant-numeric: tabular-nums; white-space: nowrap; }
.nav-readout.is-filtered { color: var(--kron-selected-ink); }
.nav-readout.is-filtered b {
    display: inline-block; margin-right: 6px; padding: 0 7px; border: 1px solid var(--kron-selected-edge); border-radius: 999px;
    background: var(--kron-selected); font-weight: 600; line-height: 18px;
}
.nav-readout-short { display: none; }
.nav-row.nav-compact .nav-readout-long { display: none; }
.nav-row.nav-compact .nav-readout-short { display: inline; }
.nav-row.nav-minimal .nav-readout { display: none; }
.nav-clear {
    border: 0; border-radius: 6px; background: none; padding: 4px 6px; color: var(--kron-ink-soft);
    font: 400 var(--kron-fs-control)/20px var(--kron-font); cursor: pointer;
}
.nav-clear:hover { background: var(--kron-hover); }
.nav-clear[hidden] { display: none; }
.nav-filters-toggle {
    display: inline-flex; align-items: center; gap: 6px; height: 28px; padding: 0 10px; border: 1px solid var(--kron-control-border);
    border-radius: 6px; background: var(--kron-field); color: var(--kron-ink-soft); font: 400 var(--kron-fs-control)/16px var(--kron-font); cursor: pointer;
}
.nav-filters-toggle:hover { background: var(--kron-hover); }
.nav-filters-toggle.is-active { border-color: var(--kron-selected-edge); color: var(--kron-selected-ink); }
.nav-filters-toggle[aria-expanded="true"] { background: var(--kron-selected); border-color: var(--kron-selected-edge); color: var(--kron-selected-ink); font-weight: 600; }
.nav-clear:focus-visible, .nav-filters-toggle:focus-visible, .filters-close:focus-visible, .filters-empty-clear:focus-visible { outline: 2px solid var(--kron-accent); outline-offset: 1px; }
/* while the box is in the bar its own header carries the readout and Clear All */
.nav-filters.riding .nav-readout, .nav-filters.riding .nav-clear { display: none; }
/* the box in the bar: the bar's top section, scrolling inside past its cap; the list under it keeps a window's height,
   so a short result cannot pull the pinned bar back down */
.report-nav > .filtering-box { margin: 0; border: 0; border-bottom: 1px solid var(--kron-hairline-soft); border-radius: 0; box-shadow: none; background: transparent; overflow-y: auto; }
.report-nav.has-filters ~ .report-panels { min-height: 100vh; }
.filters-close {
    display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px; margin-left: 4px; border: 0;
    border-radius: 6px; background: none; color: var(--kron-muted); cursor: pointer;
}
.filters-close:hover { background: var(--kron-hover); color: var(--kron-ink); }
.filters-close[hidden] { display: none; }
/* no scenario left: said where the list was, with the way back */
.filters-empty {
    display: flex; align-items: center; justify-content: center; gap: 12px; margin: 0 0 14px; padding: 28px 16px;
    background: var(--kron-card); border: 1px solid var(--kron-hairline); border-radius: 12px; color: var(--kron-muted); font-size: var(--kron-fs-body);
}
.filters-empty[hidden] { display: none; }
.filters-empty-clear {
    border: 1px solid var(--kron-control-border); border-radius: 6px; background: var(--kron-field); color: var(--kron-ink-soft);
    padding: 4px 10px; font: 400 var(--kron-fs-control)/18px var(--kron-font); cursor: pointer;
}
.filters-empty-clear:hover { background: var(--kron-hover); }
.nav-mark { height: 0; }
@media (min-width: 768.02px) {
    /* Filters only while the bar floats: at rest the box is in view above it. Pinned, the bar squares its corners and
       casts its shadow (D25). */
    .toolbar-row.report-nav:not(.k-floating) .nav-filters-toggle { display: none; }
    .toolbar-row.report-nav.is-pinned { border-radius: 0; box-shadow: 0 2px 8px var(--kron-shadow); }
}
/* on a phone the bar scrolls away and the box has its own Filters toggle */
@media (max-width: 768px) { .nav-filters { display: none; } }
'''

EDITS = [
('delete', None, '.report-nav.on-features .report-tabs', None),
('after', None, '#searchbar.kron-q-bad', SEARCH),
('after', None, '.report-panels, .report-panels *', BAR),
('body', None, '.kron-deep-chip', '''
    position: absolute;
    right: 3em;
    top: 50%;
    transform: translateY(-50%);
    font-size: 0.75em;
    padding: 0.15em 0.6em;
    border-radius: 1em;
    background: var(--kron-hover);
    color: var(--kron-muted);
    border: 1px solid var(--kron-hairline);
    line-height: 1.4;
    white-space: nowrap;
    pointer-events: none;
'''),
]
