# S4: the Features tools line (stylesheet inventory row 6; the preview's c-round2.css 55-69 and 125-136, c-round6.css
# 29-35, c-round7.css 41-44, c-round9.css 39-49, c-round17.css 84-91 and 162-163, c-contrast.css 64 and 69) and the
# features table as the Summary view (row 9b; c-extra.css 19-31, c-round2.css 74-87, c-round4.css 10, c-round5.css 165).
EDITS = [
('delete', None, '.collapse-expand-all', None),
('delete', None, '.collapse-expand-all:hover', None),
('after', None, '.features-tools .toolbar-right', r'''
        /* The Features tools (5.0.0, S4): Full and Summary, the Features tab's own tabs drawn one step below the report's
           (13 px to its 14, the same accent underline); a short rule keeps them apart from the fold groups beside them. */
        .view-switch { position: relative; display: inline-flex; gap: 18px; margin-right: 8px; padding-right: 18px; }
        .view-switch::after { content: ''; position: absolute; right: 0; top: 50%; width: 1px; height: 18px; margin-top: -9px; background: var(--kron-divider); }
        .view-btn {
            margin: 0; border: 0; border-radius: 0; background: none; padding: 4px 1px; cursor: pointer;
            font: 600 var(--kron-fs-control)/20px var(--kron-font); color: var(--kron-ink-quiet); box-shadow: inset 0 -2px 0 transparent;
        }
        .view-btn:hover { color: var(--kron-ink); }
        .view-btn[aria-selected="true"], .view-btn[aria-selected="true"]:hover { color: var(--kron-accent); box-shadow: inset 0 -2px 0 var(--kron-accent); }
        .view-btn:focus-visible, .fold-btn:focus-visible, .sort-select:focus-visible, .tools-gear:focus-visible { outline: 2px solid var(--kron-accent); outline-offset: 1px; }
        /* expand and collapse, per level, as a label and two icon buttons (item R17.2); one with nothing to do is dimmed */
        .fold-group { display: inline-flex; align-items: center; gap: 2px; margin-left: 12px; }
        .fold-group + .fold-group { margin-left: 14px; }
        .fold-label, .sort-label {
            font-size: var(--kron-fs-label); font-weight: 600; letter-spacing: var(--kron-label-track); line-height: 16px;
            text-transform: uppercase; color: var(--kron-label);
        }
        .fold-label { margin-right: 4px; }
        .fold-btn {
            display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px; padding: 0;
            border: 1px solid transparent; border-radius: 5px; background: transparent; color: var(--kron-ink-soft); cursor: pointer;
        }
        .fold-btn:hover { background: var(--kron-hover); color: var(--kron-ink); }
        .fold-btn[aria-disabled="true"] { opacity: 0.38; cursor: default; background: transparent; color: var(--kron-ink-soft); }
        .fold-btn svg { width: 16px; height: 16px; display: block; }
        .tools-spacer { flex: 1 1 auto; }
        /* the sort, at the right end: the order holds for both views */
        .sort-group { display: inline-flex; align-items: center; gap: 8px; }
        .sort-select {
            appearance: none; -webkit-appearance: none; margin: 0; padding: 5px 22px 5px 9px; cursor: pointer;
            font: 13px/1.25 var(--kron-font); color: var(--kron-ink-soft); border: 1px solid var(--kron-select-border); border-radius: 6px;
            background: var(--kron-card) url("data:image/svg+xml;charset=utf-8,%3Csvg xmlns='http://www.w3.org/2000/svg' width='8' height='5'%3E%3Cpath d='M0 0l4 5 4-5z' fill='%236a7280'/%3E%3C/svg%3E") no-repeat right 7px center;
        }
        .sort-select:hover { border-color: var(--kron-ink-quiet); }
        .tools-gear { display: none; }
        .tools-gear:hover { background: var(--kron-hover); }
        /* the Summary view: the tools that act on the cards hide with them (item R17.28) */
        .features-table-mode .toolbar-right, .features-table-mode .fold-group { display: none; }
        .features-table-mode .view-switch::after { content: none; }
        @media (min-width: 768.02px) {
            /* the views' underline sits on the line under the tools, as the report's tabs sit on theirs */
            .features-tools-line > .view-switch { align-self: stretch; margin-top: -8px; margin-bottom: -8px; }
            .features-tools-line > .view-switch > .view-btn { padding: 0 1px; }
        }
'''),
('delete', None, '.features-summary-details', None),
('delete', None, '.features-summary-details summary', None),
('body', None, '.features-summary-table-wrapper', '''            padding: 0;
            overflow-x: auto;'''),
('body', None, '.feature-summary-table', '''            width: 100%;
            border-collapse: collapse;
            font-size: 12.5px;
            color: var(--kron-ink-soft);'''),
('body', None, '.feature-summary-table th, .feature-summary-table td', '''            border: none;
            border-bottom: 1px solid var(--kron-hairline-soft);
            padding: 5px 12px;
            text-align: left;'''),
('body', None, '.feature-summary-table th', '''            padding: 7px 12px;
            font-size: 10.5px;
            font-weight: 600;
            letter-spacing: 0.06em;
            text-transform: uppercase;
            color: var(--kron-label-soft);
            background: var(--kron-strip);
            cursor: pointer;
            user-select: none;'''),
('body', None, '.feature-summary-table th:hover', '''            color: var(--kron-ink);'''),
('body', None, '.feature-summary-table tr.failed td', '''            background: var(--kron-fail-soft);'''),
('after', None, '.feature-summary-table tr.failed td', r'''
        /* The features table is the Features tab's Summary view (5.0.0, S4): a card beside the feature cards, a row above
           the headers naming the column groups, each group's first column ruled off, and a row opening its feature. */
        .features-table-view {
            margin: 0 0 14px; overflow: hidden; background: var(--kron-card);
            border: 1px solid var(--kron-hairline); border-radius: 12px; box-shadow: var(--kron-card-shadow);
        }
        .feature-summary-table thead tr:last-child th { border-bottom: 1px solid var(--kron-hairline); }
        .feature-summary-table tr.table-groups th { padding: 8px 12px 2px; text-align: center; border-bottom: none; color: var(--kron-label); cursor: default; }
        .feature-summary-table tr.table-groups th:hover { color: var(--kron-label); }
        .feature-summary-table td { vertical-align: baseline; font-variant-numeric: tabular-nums; }
        .feature-summary-table th:not(:first-child), .feature-summary-table td:not(:first-child) { text-align: right; }
        .feature-summary-table tr.table-groups th:not(:first-child) { text-align: center; }
        .feature-summary-table td:not(:first-child) { font-family: var(--kron-mono); font-size: 12px; }
        .feature-summary-table tr:last-child td { border-bottom: none; }
        .feature-summary-table .grp-start { border-left: 1px solid var(--kron-hairline); }
        .feature-summary-table tbody tr { cursor: pointer; }
        .feature-summary-table tbody tr:hover td { background: var(--kron-hover); }
        .feature-summary-table tbody tr.failed td:first-child { color: var(--kron-fail); font-weight: 600; }
        .feature-summary-table tbody tr.failed td:nth-child(4) { color: var(--kron-fail); font-weight: 650; }
        /* a feature's name opens it: a button, so the keyboard reaches it, drawn as the cell's own text */
        .feature-open { margin: 0; padding: 0; border: 0; background: none; font: inherit; color: inherit; text-align: inherit; cursor: pointer; }
        .feature-open:hover { color: var(--kron-accent); text-decoration: underline; text-underline-offset: 2px; }
        .feature-open:focus-visible { outline: 2px solid var(--kron-accent); outline-offset: 2px; }
'''),
('append', '@media (max-width: 768px)', None, r'''
            /* the Features tools on a phone: the views and the sort on one line, the fold groups and the gear on the next,
               and the display settings behind the gear */
            .features-tools-line.toolbar-left { gap: 8px; padding: 10px 14px; }
            .view-switch { order: 1; }
            .sort-group { order: 1; margin-left: auto; }
            .tools-spacer { display: none; }
            .fold-group { order: 4; margin-left: 0; }
            .tools-gear {
                display: inline-block; order: 5; margin-left: auto; padding: 6px 9px; cursor: pointer;
                font-size: 15px; line-height: 1; color: var(--kron-ink-soft); background: var(--kron-card);
                border: 1px solid var(--kron-select-border); border-radius: 6px;
            }
            .tools-open .tools-gear { background: var(--kron-selected); color: var(--kron-selected-ink); border-color: var(--kron-selected-edge); }
            .report-nav:not(.tools-open) .features-tools .toolbar-right { display: none; }
            .features-tools .toolbar-right { padding: 0 14px 10px; }
'''),
]
