# S2: the run band (structure inventory D3, D4; stylesheet inventory 2, rows 2 and 3). The 4.x header row, its
# summary and CI tables and the pie's wrapper go; the band's rules take their place, from the preview's round 2 and
# type scale, with the ring coloured through classes instead of the preview's attribute selectors.
EDITS = [
('delete', None, '.column-header', None),
('delete', None, '.test-execution-summary', None),
('delete', None, '.test-execution-summary h2', None),
('delete', None, '.test-execution-summary table td', None),
('delete', None, '.summary-chart', None),
('delete', None, '.ci-chart-group', None),
('delete', None, '.ci-metadata', None),
('delete', None, '.ci-metadata table', None),
('delete', None, '.ci-metadata td:first-child', None),
('delete', None, '.ci-metadata td:last-child', None),
('delete', '@media (min-width: 768.02px)', '.ci-metadata', None),
('delete', '@media (min-width: 768.02px) and (max-width: 1160px)', '.header-row', None),
('delete', '@media (max-width: 768px)', '.header-row', None),
('delete', '@media (max-width: 768px)', '.test-execution-summary', None),
('delete', '@media (max-width: 768px)', '.summary-chart', None),
('delete', '@media (max-width: 768px)', '.ci-chart-group', None),
('delete', '@media (max-width: 768px)', '.ci-metadata', None),
('delete', '@media (max-width: 480px)', '.header-row', None),
('delete', '@media (max-width: 480px)', '.summary-chart', None),
('after', None, '.header-row', r'''

/* The run band (5.0.0): the ring, the test execution summary and the CI zone on one row while all three fit
   whole; otherwise report-run-band-script.js sets .ci-below and the CI zone takes a second row under the summary. */
.run-band {
    display: flex; flex-wrap: wrap; align-items: center; gap: 12px 24px; margin: 0 0 14px; padding: 12px 20px;
    background: var(--kron-card); border: 1px solid var(--kron-hairline); border-radius: 12px; box-shadow: var(--kron-card-shadow);
}
.run-ring { flex: 0 0 64px; width: 64px; height: 64px; }
.run-ring svg { display: block; width: 64px; height: 64px; }
.run-ring .ring-pass { stroke: var(--kron-pass-ring); }
.run-ring .ring-fail { stroke: var(--kron-fail); }
.run-ring .ring-skip { stroke: var(--kron-dot); }
.run-ring .ring-bypass { stroke: var(--kron-bypass); }
.run-ring .ring-rate { fill: var(--kron-ink); font-family: var(--kron-font); font-size: 22px; font-weight: 650; }
.run-zone { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
.run-band:not(.ci-below) .run-ci { padding-left: 24px; border-left: 1px solid var(--kron-hairline-soft); }
.run-band.ci-below { display: grid; grid-template-columns: 64px minmax(0, 1fr); column-gap: 24px; row-gap: 10px; align-items: start; }
.run-band.ci-below .run-ring { grid-row: 1 / span 2; align-self: center; }
.run-band.ci-below .run-ci { grid-column: 2; padding-top: 10px; border-top: 1px solid var(--kron-hairline-soft); }
.run-head { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px; }
.run-title { margin: 0; font-size: 14px; font-weight: 600; color: var(--kron-ink); }
.run-status {
    padding: 1px 8px; border-radius: 999px; background: var(--kron-pass-soft); color: var(--kron-pass);
    font-size: var(--kron-fs-label); font-weight: 600; line-height: 16px; letter-spacing: var(--kron-label-track); text-transform: uppercase;
}
.run-status-failed { background: var(--kron-fail-soft); color: var(--kron-fail); }
.run-when { font-size: 12px; color: var(--kron-muted); font-variant-numeric: tabular-nums; }
.run-stats { display: flex; flex-wrap: wrap; gap: 4px 22px; margin: 0; }
.run-stat { display: flex; flex-direction: column; min-width: 0; }
.run-stat dt {
    font-size: var(--kron-fs-label); font-weight: 600; line-height: 16px; letter-spacing: var(--kron-label-track);
    text-transform: uppercase; color: var(--kron-label);
}
.run-stat dd {
    margin: 0; font-family: var(--kron-font); font-size: 13px; line-height: 1.45; color: var(--kron-ink);
    font-variant-numeric: tabular-nums; overflow-wrap: anywhere;
}
.run-stat-fail dd { color: var(--kron-fail); font-weight: 600; }
.run-stat dd code { padding: 0; background: none; font-family: var(--kron-mono); font-size: 13px; }
.run-link { color: var(--kron-accent); font-family: var(--kron-font); text-decoration: none; }
.run-link:hover { text-decoration: underline; }
'''),
('delete', None, '.header-row', None),
('append', '@media (max-width: 768px)', None, r'''
            .run-band { gap: 10px 16px; padding: 12px 16px; }
            .run-band.ci-below { grid-template-columns: 52px minmax(0, 1fr); column-gap: 14px; }
            .run-ring, .run-ring svg { width: 52px; height: 52px; flex-basis: 52px; }
'''),
]
