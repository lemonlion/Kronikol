# S11g: the report's diagnostics on the theme's tokens (the preview's c-round2.css 89-92 and 117-120, c-round7.css 87
# and 92; ss 248): a card whose heading is no disclosure in its tab (the tab is one, S3) but stays one on the labs
# page, one rule between items, each kind in its own colour (a failure red, a degraded capture amber, the rest
# neutral). Not ported (ss 3.8 items 2 and 3): the preview's doubled separators and every kind drawn amber. Each
# colour falls back to its 4.x value, for a page without the token sheet. usage: python scripts-s11g.py <root>
import sys
P = sys.argv[1].rstrip('/') + '/src/Kronikol/Reports/report-diagnostics-styles.css'
s = open(P, encoding='utf-8', newline='').read()
a = s.index('        /* Report diagnostics (capture health, skipped lines, render failures) */')
b = s.index('        /* in its tab (5.0.0, plans/OPTION_C_PLAN.md S3) */')
assert a < b
new = '''        /* Report diagnostics (capture health, skipped lines, render failures), on the theme's tokens (5.0.0,
           plans/OPTION_C_PLAN.md S11): a card, one rule between items, and each kind in its own colour, a failure red
           and a degraded capture amber. Each colour falls back to its 4.x value where a page has no token sheet (the
           labs page, where the heading stays a disclosure). */
        .report-diagnostics {
            margin: 1em 0;
            border: 1px solid var(--kron-hairline, rgb(224, 200, 160));
            border-radius: 12px;
            background: var(--kron-card, rgb(255, 251, 243));
            padding: 0;
            overflow: hidden;
        }
        .report-diagnostics > summary {
            padding: 12px 16px;
            font-weight: 600;
            font-size: 14px;
            color: var(--kron-ink, rgb(130, 85, 0));
            cursor: pointer;
        }
        .report-diagnostics[open] > summary { border-bottom: 1px solid var(--kron-hairline-soft, rgb(240, 232, 215)); }
        /* in its tab the heading is no disclosure (report-tabs-script.js keeps it open); on the labs page it stays one */
        .report-panel > .report-diagnostics > summary { list-style: none; cursor: default; }
        .report-panel > .report-diagnostics > summary::-webkit-details-marker { display: none; }
        .report-diagnostics-list {
            list-style: none;
            margin: 0;
            padding: 2px 16px 8px;
            font-size: 13px;
        }
        .report-diagnostics-list > li {
            padding: 10px 0;
            border-top: 1px solid var(--kron-hairline-soft, rgb(240, 232, 215));
            line-height: 1.55;
            color: var(--kron-ink-soft);
            overflow-wrap: anywhere;
        }
        .report-diagnostics-list > li:first-child { border-top: none; }
        .report-diagnostic-kind {
            display: inline-block;
            background: var(--kron-strip, rgb(224, 224, 224));
            color: var(--kron-ink-soft, rgb(60, 60, 60));
            border-radius: 999px;
            padding: 1px 8px;
            font-family: var(--kron-mono);
            font-size: 11px;
            font-weight: 600;
            margin-right: 8px;
            white-space: nowrap;
        }
        .report-diagnostic-kind-capturedegraded {
            background: var(--kron-warn-soft, rgb(255, 221, 150));
            color: var(--kron-warn, rgb(110, 70, 0));
        }
        .report-diagnostic-kind-renderfailure,
        .report-diagnostic-kind-outputfailure,
        .report-diagnostic-kind-attachmentfailure {
            background: var(--kron-fail-soft, rgb(255, 210, 210));
            color: var(--kron-fail, rgb(140, 0, 0));
        }
        .report-diagnostic-scenario {
            color: var(--kron-muted, rgb(100, 100, 100));
            font-size: 0.85em;
        }

'''
s = s[:a] + new + s[b:]
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
