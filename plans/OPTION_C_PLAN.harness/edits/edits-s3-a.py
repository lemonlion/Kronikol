# S3: the report bar, its tabs and panels (D20; stylesheet inventory 5, the preview's c-skin.css 742-755, 774, 825,
# 925, 1206, 1224, 1402-1411, 1428 and 1613). Sticky on a desktop, static on a phone (S12 keeps the one 768.02 px
# breakpoint, where the preview had 769). The pinned state and the filters riding in the bar are S7's.
EDITS = [
('after', None, '.toolbar-right', r'''

/* The report bar (5.0.0): the tabs, and on the Features tab its tools in a strip under them, on a card that sticks to
   the top of the window. On a desktop the bar runs on into its panel, the two reading as one container on a tint
   between the page and the cards. */
.toolbar-row.report-nav {
    display: block; position: sticky; top: env(safe-area-inset-top, 0px); z-index: 40; padding: 0; margin: 0 0 14px;
    background: var(--kron-card); border: 1px solid var(--kron-hairline); border-radius: 12px; box-shadow: var(--kron-card-shadow);
}
.report-tabs { display: flex; gap: 2px; padding: 0 10px; overflow-x: auto; scrollbar-width: none; }
.report-tabs::-webkit-scrollbar { display: none; }
.report-nav.on-features .report-tabs { border-bottom: 1px solid var(--kron-hairline-soft); }
.report-tab {
    flex: none; margin: 0; padding: 12px 12px 11px; border: none; border-radius: 6px 6px 0 0; background: none;
    font: 600 14px/1.25 var(--kron-font); color: var(--kron-ink-quiet); white-space: nowrap; cursor: pointer;
    box-shadow: inset 0 -2px 0 transparent; transition: background .12s, color .12s;
}
.report-tab:hover { color: var(--kron-ink); background: var(--kron-hover); }
.report-tab[aria-selected="true"], .report-tab[aria-selected="true"]:hover { color: var(--kron-accent); background: none; box-shadow: inset 0 -2px 0 var(--kron-accent); }
.report-tab:focus-visible { outline: 2px solid var(--kron-accent); outline-offset: -3px; }
.tab-n { margin-left: 6px; font: 400 var(--kron-fs-label) var(--kron-font); color: var(--kron-faint); font-variant-numeric: tabular-nums; }
.report-tab[aria-selected="true"] .tab-n { color: var(--kron-selected-ink); }
.features-tools { background: var(--kron-strip); border-radius: 0 0 12px 12px; }
.report-nav:not(.on-features) .features-tools { display: none; }
.features-tools-line { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 8px; padding: 8px 14px; }
.features-tools .toolbar-right {
    display: flex; flex-wrap: wrap; align-items: center; column-gap: 14px; row-gap: 6px; padding: 7px 14px 8px;
    border-top: 1px solid var(--kron-divider-soft);
}
.report-panel > .scenario-timeline, .report-panel > .component-diagram-section, .report-panel > .history-section,
.report-panel > .report-diagnostics { margin: 0 0 14px; }
/* what scrolls into view inside the panels stops below the bar while it sticks (report-tabs-script.js sets the height) */
.report-panels, .report-panels * { scroll-margin-top: calc(var(--kron-nav-h, 0px) + 12px); }
@media (min-width: 768.02px) {
    .toolbar-row.report-nav { margin-bottom: 0; border-radius: 12px 12px 0 0; box-shadow: none; }
    .toolbar-row.report-nav ~ .report-panels {
        margin-bottom: 14px; padding: 14px 14px 0; background: var(--kron-panel);
        border: 1px solid var(--kron-hairline); border-top: 0; border-radius: 0 0 12px 12px;
    }
    .toolbar-row.report-nav .features-tools { border-radius: 0; }
}
'''),
('append', '@media (max-width: 768px)', None, r'''
            /* a phone keeps its bar in the page, a card like the others: there is no room to give a sticky one */
            .toolbar-row.report-nav { position: static; }
'''),
]
