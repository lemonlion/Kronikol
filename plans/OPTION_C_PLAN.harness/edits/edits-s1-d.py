# S1 commit 2, part d: parameter tables, step tables and inline values (stylesheet inventory 2, row 13), with round 16's
# one even border in a rounded frame. The rows lose the 4.x status tints, as in the preview: the Status column's badge
# says it, and the hover and selected states read the same on every row.
WRAPPED = '.param-table-wrapper > .param-test-table'
EDITS = [
('body', None, '.step-param-inline', r'''
padding: 1px 4px;
border-radius: 3px;
font-family: var(--kron-mono);
font-size: 0.9em;
color: var(--kron-ink);
'''),
('body', None, '.param-success', 'background: var(--kron-pass-soft);'),
('body', None, '.param-failure', 'background: var(--kron-fail-soft);'),
('body', None, '.param-exception', 'background: var(--kron-exception-soft);'),
('body', None, '.param-not-provided', 'background: var(--kron-warn-soft);'),
('body', None, '.param-na', 'background: var(--kron-code-bg);'),
('body', None, '.step-param-inline pre, .step-param-table td pre, .step-param-combined-table td pre, .tree-node pre', r'''
display: inline;
margin: 0;
padding: 0.1em 0.3em;
background: color-mix(in srgb, var(--kron-ink) 7%, transparent);
border-radius: 3px;
font-family: var(--kron-mono);
font-size: 0.92em;
font-style: italic;
color: var(--kron-faint);
'''),
('body', None, '.step-param-table', r'''
margin: 4px 0 4px 25px;
overflow-x: auto;
'''),
('body', None, '.step-param-table th, .step-param-table td', r'''
padding: 3px 8px;
border: 1px solid var(--kron-hairline);
font-size: var(--kron-fs-meta);
font-variant-numeric: tabular-nums;
'''),
('body', None, '.step-param-table th', r'''
background: var(--kron-strip);
color: var(--kron-ink-soft);
font-weight: 600;
'''),
('body', None, '.step-param-combined-table th.col-highlight, .step-param-combined-table td.col-highlight', r'''
background-color: var(--kron-selected);
border-color: var(--kron-selected-edge);
'''),
('body', None, '.step-param-highlight', r'''
background-color: var(--kron-warn-soft) !important;
transition: background-color 1.5s ease;
'''),
('body', None, '.step-param-expand', r'''
display: block;
margin: 4px 0 4px 6px;
padding: 6px 10px;
max-height: 300px;
overflow-y: auto;
background: var(--kron-strip);
color: var(--kron-ink);
border: 1px solid var(--kron-hairline);
border-radius: 3px;
font-family: var(--kron-mono);
font-size: 0.82em;
white-space: pre-wrap;
word-break: break-word;
'''),
('body', None, '.step-param-combined-table', r'''
margin: 8px 0 4px 25px;
overflow-x: auto;
'''),
('body', None, '.step-param-combined-table th, .step-param-combined-table td', r'''
padding: 3px 8px;
border: 1px solid var(--kron-hairline);
font-size: var(--kron-fs-meta);
font-variant-numeric: tabular-nums;
'''),
('body', None, '.step-param-combined-table th', r'''
background: var(--kron-strip);
color: var(--kron-ink-soft);
font-weight: 600;
'''),
('body', None, '.step-param-combined-table th.combined-separator, .step-param-combined-table td.combined-separator', r'''
width: 1em;
padding: 3px 4px;
background: var(--kron-field);
border-left: 2px solid var(--kron-dot);
border-right: 2px solid var(--kron-dot);
text-align: center;
font-weight: bold;
'''),
('body', None, '.step-param-tree', r'''
margin: 4px 0 4px 24px;
font-family: var(--kron-mono);
font-size: 0.85em;
'''),
('body', None, '.param-test-table', r'''
margin: 0.5em 0 0.5em 1em;
width: 100%;
border-collapse: collapse;
font-size: 12.5px;
'''),
('body', None, '.param-test-table th, .param-test-table td', r'''
border: 1px solid var(--kron-hairline);
text-align: left;
'''),
('after', None, '.param-test-table th, .param-test-table td', r'''

.param-test-table th {
    padding: 6px 10px;
    background: var(--kron-strip);
    color: var(--kron-label-soft);
    font-size: var(--kron-fs-label);
    font-weight: 600;
    letter-spacing: var(--kron-label-track);
    text-transform: uppercase;
}
.param-test-table td {
    padding: 5px 10px;
    color: var(--kron-ink);
    vertical-align: baseline;
    font-size: var(--kron-fs-meta);
    font-variant-numeric: tabular-nums;
}
.param-test-table tr:last-child td { border-bottom: none; }
'''),
('body', None, '.param-test-table thead', 'background: var(--kron-strip);'),
('body', None, '.param-test-table .master-header', r'''
text-align: center;
background: var(--kron-strip);
color: var(--kron-label);
font-weight: 600;
'''),
('body', None, '.param-test-table .sub-header', r'''
font-size: var(--kron-fs-label);
font-weight: 600;
line-height: 16px;
letter-spacing: var(--kron-label-track);
'''),
('body', None, '.param-test-table tbody tr', r'''
cursor: pointer;
transition: background 0.15s;
'''),
('body', None, '.param-test-table tbody tr:hover', 'background: var(--kron-hover);'),
('body', None, '.param-test-table tbody tr.row-active', r'''
background: var(--kron-selected);
font-weight: 400;
'''),
('after', None, '.param-test-table tbody tr.row-active', r'''

.param-test-table tbody tr.row-active:hover { background: var(--kron-selected); }
.param-test-table tbody tr.row-active td:first-child { box-shadow: inset 2px 0 0 var(--kron-accent); }
'''),
('delete', None, '.param-test-table tbody tr.row-passed', None),
('delete', None, '.param-test-table tbody tr.row-failed', None),
('delete', None, '.param-test-table tbody tr.row-skipped', None),
('delete', None, '.param-test-table tbody tr.row-bypassed', None),
('delete', None, '.param-test-table tbody tr.row-passed:hover', None),
('delete', None, '.param-test-table tbody tr.row-failed:hover', None),
('delete', None, '.param-test-table tbody tr.row-skipped:hover', None),
('delete', None, '.param-test-table tbody tr.row-bypassed:hover', None),
('delete', None, '.param-test-table tbody tr.row-active.row-passed', None),
('delete', None, '.param-test-table tbody tr.row-active.row-failed', None),
('delete', None, '.param-test-table tbody tr.row-active.row-skipped', None),
('delete', None, '.param-test-table tbody tr.row-active.row-bypassed', None),
('body', None, '.param-test-table tbody tr.row-search-match', 'box-shadow: inset 2px 0 0 var(--kron-accent);'),
('body', None, '.param-test-table .mono', r'''
font-family: var(--kron-mono);
font-size: var(--kron-fs-meta);
'''),
('body', None, '.param-test-table tbody tr.examples-block-row, .param-test-table tbody tr.examples-block-row:hover', r'''
cursor: default;
background: var(--kron-strip);
'''),
('body', None, '.param-test-table tbody tr.examples-block-row td', r'''
padding: 0.25em 0.6em;
border-top: 1px solid var(--kron-hairline);
color: var(--kron-muted);
'''),
('body', None, '.examples-block-row .examples-block-counts', r'''
margin-left: 0.8em;
padding: 0.1em 0.5em;
background: var(--kron-field);
color: var(--kron-muted);
border-radius: 3px;
font-size: 0.8em;
'''),
('body', None, '.examples-block-row .examples-block-desc', r'''
display: block;
margin-top: 0.15em;
font-size: 0.85em;
font-style: italic;
color: var(--kron-muted);
'''),
('body', None, '.param-table-wrapper', r'''
margin: 2px 0 10px;
overflow-x: auto;
background: var(--kron-card);
border: 1px solid var(--kron-hairline);
border-radius: 8px;
'''),
('after', None, '.param-table-wrapper', r'''

/* One even border with rounded corners: the frame draws the outer border and clips the corners, the cells only the
   lines between them (a cell's right and bottom edges, except where they meet the frame). */
.param-table-wrapper > .param-test-table { margin: 0; border-collapse: separate; border-spacing: 0; }
.param-table-wrapper > .param-test-table th,
.param-table-wrapper > .param-test-table td { border-style: solid; border-color: var(--kron-hairline); border-width: 0 1px 1px 0; }
.param-table-wrapper > .param-test-table tbody tr > :last-child,
.param-table-wrapper > .param-test-table thead tr:first-child > :last-child { border-right-width: 0; }
/* the header's second row reaches the right edge only when the first row's last cell does not span down to it */
.param-table-wrapper > .param-test-table thead:not(:has(tr:first-child > :last-child[rowspan])) tr + tr > :last-child { border-right-width: 0; }
.param-table-wrapper > .param-test-table tbody tr:last-child > td { border-bottom-width: 0; }
.param-table-wrapper > .param-test-table tbody tr.examples-block-row td { border-top-width: 0; }
'''),
('body', None, '.status-badge', r'''
display: inline-block;
padding: 1px 8px;
border: 1px solid transparent;
border-radius: 999px;
font-size: var(--kron-fs-label);
font-weight: 600;
line-height: 16px;
white-space: nowrap;
'''),
('body', None, '.status-badge.badge-pass', 'background: var(--kron-pass-soft);\ncolor: var(--kron-pass);'),
('body', None, '.status-badge.badge-fail', 'background: var(--kron-fail-soft);\ncolor: var(--kron-fail);\nborder-color: var(--kron-fail-edge);'),
('body', None, '.status-badge.badge-skip', 'background: var(--kron-skip-soft);\ncolor: var(--kron-skip);'),
('body', None, '.status-badge.badge-bypass', 'background: var(--kron-bypass-soft);\ncolor: var(--kron-bypass);'),
('body', None, '.param-diagram-identical-badge', r'''
display: inline-block;
margin-bottom: 0.5em;
padding: 0.2em 0.6em;
background: var(--kron-pass-soft);
color: var(--kron-pass);
border-radius: 4px;
font-size: 0.85em;
font-weight: 500;
'''),
('body', None, '.cell-subtable th', r'''
padding: 0.2em 0.4em;
background: var(--kron-strip);
color: var(--kron-label);
border: 1px solid var(--kron-hairline-soft);
font-size: 0.85em;
font-weight: 600;
'''),
('body', None, '.cell-subtable td', r'''
padding: 0.2em 0.4em;
border: 1px solid var(--kron-hairline-soft);
font-family: var(--kron-mono);
font-size: 0.92em;
'''),
('body', None, 'details.param-expand > summary', r'''
padding: 0.15em 0;
list-style: none;
font-size: 0.9em;
color: var(--kron-ink-soft);
cursor: pointer;
'''),
('body', None, 'details.param-expand > summary::before', r'''
content: "▸ ";
font-size: 0.8em;
color: var(--kron-faint);
'''),
('body', None, 'details.param-expand .expand-body', r'''
margin: 0.3em 0 0.2em 0.5em;
padding: 0.5em 0.7em;
background: var(--kron-strip);
color: var(--kron-ink);
border: 1px solid var(--kron-hairline);
border-radius: 4px;
font-family: var(--kron-mono);
font-size: 0.88em;
line-height: 1.4;
white-space: pre-wrap;
'''),
('body', None, 'details.param-expand .expand-body .prop-key', 'color: var(--kron-prop-key);'),
('body', None, 'details.param-expand .expand-body .prop-val', 'color: var(--kron-prop-val);'),
]
