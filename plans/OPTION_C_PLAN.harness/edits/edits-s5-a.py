# S5 and S6: the filter rows (stylesheet inventory rows 2 and 6; the preview's c-skin.css 216-250, 310-352, 416-423, 430-441,
# 530-563, 843-879, 888-895, 930-945, 959-979, 988-996, 1126-1128, 1162-1166, 1211-1236, 1247-1249, 1555-1559 and 1642, the
# rounds as the skin composes them). The layout the preview measures (.pair-fits, .trio-fits: Duration and Happy paths
# beside Status when they fit whole) is S7's; until then each takes a line of its own, as the preview does when they do not fit.
FILTER_ROWS = r'''
        /* The filter rows (5.0.0, S5 and S6). Each row is a label in a 92 px column and 8 px, then its controls in a
           wrapping flex of their own, so a second line of chips stays in the control column and the label sits on the
           first chip's baseline. Every boxed control is 28 px: a 16 px line, 5 px of padding each side and the border. */
        .status-filters, .duration-filters, .happy-filters, .tag-filters, .dependency-filters, .category-filters { display: flex; align-items: baseline; column-gap: 0; margin-top: 10px; }
        .filter-row > .status-filters, .filter-row > .duration-filters, .filter-row > .happy-filters { margin-top: 0; }
        .filter-row > .status-filters { flex: none; flex-wrap: nowrap; }
        .filter-row > .duration-filters, .filter-row > .happy-filters { flex-basis: 100%; }
        .status-filters-label, .duration-filters-label, .happy-filters-label, .tag-filters-label, .dependency-filters-label, .category-filters-label {
            flex: 0 0 92px; margin-right: 8px; color: var(--kron-label);
            font-size: var(--kron-fs-label); font-weight: 600; line-height: 16px; letter-spacing: var(--kron-label-track); text-transform: uppercase;
        }
        .fltr-controls { display: flex; flex: 1 1 0; flex-wrap: wrap; align-items: baseline; min-width: 0; row-gap: 6px; }
        .tag-filters .fltr-controls, .dependency-filters .fltr-controls, .category-filters .fltr-controls { column-gap: 6px; }
        .filter-row > .status-filters > .fltr-controls, .happy-filters > .fltr-controls { flex: none; flex-wrap: nowrap; }

        /* Status, Duration and Happy paths: segmented radio groups, the chosen segment drawn over its neighbours' edge */
        .status-toggle, .percentile-btn, .happy-seg {
            margin: 0 0 0 -1px; padding: 5px 11px; border: 1px solid var(--kron-control-border); border-radius: 0;
            background: var(--kron-card); color: var(--kron-ink-soft); font: var(--kron-fs-control)/16px var(--kron-font); cursor: pointer;
        }
        .status-toggle:first-of-type, .percentile-btn:first-of-type, .happy-seg:first-of-type { margin-left: 0; border-radius: 6px 0 0 6px; }
        .status-toggle:last-of-type, .percentile-btn:last-of-type, .happy-seg:last-of-type { border-radius: 0 6px 6px 0; }
        .status-toggle:hover, .percentile-btn:hover, .happy-seg:hover { background: var(--kron-hover); }
        .status-toggle.status-active, .percentile-btn.percentile-active, .happy-seg[aria-pressed="true"] {
            position: relative; z-index: 1; border-color: var(--kron-selected-edge); background: var(--kron-selected); color: var(--kron-selected-ink); font-weight: 600;
        }

        /* Tags, Dependencies and Categories: word chips. Has is the row's active class, has not its exclude class, a red
           tint with no strike through the name; a chip for the scenarios with none is in italics. */
        .tag-toggle, .dependency-toggle, .category-toggle {
            position: relative; padding: 5px 10px; border: 1px solid var(--kron-control-border); border-radius: 999px;
            background: var(--kron-card); color: var(--kron-ink-quiet); font: var(--kron-fs-control)/16px var(--kron-font); cursor: pointer;
        }
        .tag-toggle:hover, .dependency-toggle:hover, .category-toggle:hover { background: var(--kron-hover); }
        .tag-toggle.tag-active, .dependency-toggle.dependency-active, .category-toggle.category-active {
            z-index: 1; border-color: var(--kron-selected-edge); background: var(--kron-selected); color: var(--kron-selected-ink);
        }
        .tag-toggle.tag-exclude, .dependency-toggle.dependency-exclude, .category-toggle.category-exclude {
            border-color: var(--kron-exclude-edge); background: var(--kron-exclude-soft); color: var(--kron-exclude-ink);
        }
        .tag-none, .dependency-none, .category-none { font-style: italic; }

        /* a row's ANY / ALL switch is a mode, not a filter: a small square button before the chips */
        .tag-mode-toggle, .dep-mode-toggle, .cat-mode-toggle {
            margin-right: 2px; padding: 6px 9px 4px; border: 1px solid var(--kron-control-border); border-radius: 6px;
            background: var(--kron-field); color: var(--kron-ink-quiet); font: 600 11px/16px var(--kron-font); letter-spacing: 0.03em; cursor: pointer;
        }
        .tag-mode-toggle:hover, .dep-mode-toggle:hover, .cat-mode-toggle:hover { background: var(--kron-hover); }

        /* a row's Clear: a text action in the accent after its last chip, there only while one of them is set */
        .group-clear {
            align-self: center; margin-left: 2px; padding: 4px 6px; border: 0; border-radius: 4px; background: none;
            color: var(--kron-accent); font: 600 var(--kron-fs-control)/16px var(--kron-font); cursor: pointer;
        }
        .group-clear:hover { text-decoration: underline; }
        .group-clear[hidden] { display: none; }

        .status-toggle:focus-visible, .percentile-btn:focus-visible, .happy-seg:focus-visible, .tag-toggle:focus-visible,
        .dependency-toggle:focus-visible, .category-toggle:focus-visible, .tag-mode-toggle:focus-visible, .dep-mode-toggle:focus-visible,
        .cat-mode-toggle:focus-visible, .group-clear:focus-visible { outline: 2px solid var(--kron-accent); outline-offset: 1px; }

        /* what the filters leave showing, on the FILTERING line left of Clear All, set apart from the actions by a rule */
        .filter-readout {
            align-self: center; margin-right: 8px; padding-right: 12px; border-right: 1px solid var(--kron-divider);
            color: var(--kron-muted); font-size: var(--kron-fs-meta); line-height: 20px; font-variant-numeric: tabular-nums; white-space: nowrap;
        }
        .filter-readout.is-filtered { color: var(--kron-selected-ink); }
        .filter-readout.is-filtered b {
            display: inline-block; margin-right: 6px; padding: 0 7px; border: 1px solid var(--kron-selected-edge); border-radius: 999px;
            background: var(--kron-selected); font-weight: 600; line-height: 18px;
        }

        /* The rows reflow on the filter box's own width, not the window's. Under 520 px a segmented run that cannot fit
           whole becomes a grid of equal pills; under 430 px each label takes a line of its own above its controls (which
           must claim the whole line too, or they collapse to nothing beside it), and the exports drop "Filtered". */
        @container (max-width: 520px) {
            .status-filters > .fltr-controls, .duration-filters > .fltr-controls, .happy-filters > .fltr-controls { display: grid; flex: 1 1 0; gap: 6px; }
            .status-filters > .fltr-controls { grid-template-columns: repeat(4, minmax(0, 1fr)); }
            .duration-filters > .fltr-controls { grid-template-columns: repeat(2, minmax(0, 1fr)); }
            .happy-filters > .fltr-controls { grid-template-columns: repeat(3, minmax(0, 1fr)); }
            .filter-row > .status-filters { flex: 1 1 100%; flex-wrap: wrap; }
            .status-toggle, .percentile-btn, .happy-seg, .status-toggle:first-of-type, .percentile-btn:first-of-type, .happy-seg:first-of-type,
            .status-toggle:last-of-type, .percentile-btn:last-of-type, .happy-seg:last-of-type { margin: 0; border-radius: 6px; text-align: center; }
            .status-toggle { padding-left: 4px; padding-right: 4px; }
            .percentile-btn:last-of-type { grid-column: 1 / -1; }
        }
        @container (max-width: 430px) {
            .status-filters, .duration-filters, .happy-filters, .tag-filters, .dependency-filters, .category-filters { flex-wrap: wrap; }
            .status-filters-label, .duration-filters-label, .happy-filters-label, .tag-filters-label, .dependency-filters-label,
            .category-filters-label { flex-basis: 100%; margin: 0 8px 2px 0; }
            .fltr-controls { flex-basis: 100%; }
            .wide-only { display: none; }
            /* the readout wraps rather than widen the actions past the box */
            .filter-readout { white-space: normal; }
        }
        @container (max-width: 300px) {
            .status-filters > .fltr-controls { grid-template-columns: repeat(2, minmax(0, 1fr)); }
        }
'''

EDITS = [
('body', None, '.filtering-box', '''
            min-width: 0;
            margin: 0 0 14px;
            padding: 14px 16px;
            background: var(--kron-card);
            border: 1px solid var(--kron-hairline);
            border-radius: 12px;
            box-shadow: var(--kron-card-shadow);
            /* the filter rows reflow on the box's own width (S6) */
            container-type: inline-size;
'''),
('body', None, '.filters', '''
            display: flex;
            flex-direction: column;
            gap: 0;
'''),
('body', None, '.filter-row', '''
            display: flex;
            flex-wrap: wrap;
            gap: 14px;
            align-items: baseline;
            margin-top: 12px;
'''),
('body', None, '#searchbar', '''
            padding: 0.5em;
            border-radius: 0.5em;
            width: 100%;
            box-sizing: border-box;
            border: 1px solid var(--kron-select-border);
            background: var(--kron-field);
            color: var(--kron-ink);
            font-family: var(--kron-font);
            font-size: var(--kron-fs-body);
'''),
('after', None, '#searchbar:disabled', r'''
        #searchbar::placeholder { color: var(--kron-faint); opacity: 1; }
        #searchbar:focus { background: var(--kron-card); border-color: var(--kron-accent); outline: 2px solid var(--kron-accent); outline-offset: 1px; box-shadow: none; }
        /* a dep:, duration: or is: qualifier the search could not read stays in it as text, and the field says so (S6) */
        #searchbar.kron-q-bad { border-color: var(--kron-warn); }
'''),
('delete', None, '.happy-path-filters, .dependency-filters, .status-filters', None),
('delete', None, '.happy-path-filters', None),
('delete', None, '.happy-path-filters-label, .dependency-filters-label, .status-filters-label', None),
('delete', None, '.happy-path-toggle, .dependency-toggle, .status-toggle', None),
('delete', None, '.happy-path-toggle:hover, .dependency-toggle:hover, .status-toggle:hover', None),
('delete', None, '.happy-path-toggle.happy-path-active, .dependency-toggle.dependency-active, .status-toggle.status-active', None),
('delete', None, '.dep-mode-toggle', None),
('delete', None, '.dep-mode-toggle:hover', None),
('delete', None, '.cat-mode-toggle', None),
('delete', None, '.cat-mode-toggle:hover', None),
('delete', None, '.duration-filters', None),
('delete', None, '.duration-filters-label', None),
('delete', None, '.percentile-btn', None),
('delete', None, '.percentile-btn:hover', None),
('delete', None, '.percentile-btn.percentile-active', None),
('delete', None, '.category-filters', None),
('delete', None, '.category-filters-label', None),
('delete', None, '.category-toggle', None),
('delete', None, '.category-toggle:hover', None),
('delete', None, '.category-toggle.category-active', None),
('after', None, '.hp-hidden', FILTER_ROWS),
# a phone keeps the rows a column (4.x) on the top-level baseline, and the search at 16 px so focusing it does not zoom
('body', '@media (max-width: 768px)', '.filter-row', '''
                flex-direction: column;
'''),
('append', '(max-width: 768px)', None, '''
            #searchbar { font-size: 16px; }
'''),
# every filter control is a 36 px touch target on a phone (4.x left the tag chips and the Happy paths segments out)
('delete', '@media (max-width: 480px)', '.happy-path-toggle, .dependency-toggle, .status-toggle, .category-toggle, .percentile-btn', None),
('delete', '@media (max-width: 480px)', '.dependency-toggle, .category-toggle', None),
('delete', '@media (max-width: 480px)', '.dependency-filters, .category-filters', None),
('append', '(max-width: 480px)', None, '''
            /* #6 Touch target size bump: every filter control */
            .status-toggle, .percentile-btn, .happy-seg, .tag-toggle, .dependency-toggle, .category-toggle { min-height: 36px; }

            /* #5 Long chip names are cut with an ellipsis inside the box */
            .tag-toggle, .dependency-toggle, .category-toggle {
                max-width: 100%;
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            }
'''),
]
