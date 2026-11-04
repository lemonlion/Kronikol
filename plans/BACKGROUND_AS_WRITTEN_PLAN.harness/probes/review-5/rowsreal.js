// node rowsreal.js <report.html> : click sequences over outlines of the real suite's TestRunReport.html (features Q05, Q06, Q11, R05, R23, R63).
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));

const sequences = [
    ['Q14 row', 'two Examples blocks, attachments', [1, 2, 3, 0]],
    ['Q14 row', 'the grouped table', ['f', 1, 2, 3, 0, 'f', 1]],
    ['Q05 row', 'big composite, row b fails by itself', [1, 2, 0]],
    ['Q05 row', 'reader opens the big composite on row a, then row b, c', ['x:scenario-background', 'c:0', 1, 2]],
    ['Q06 row', 'first row fails in Background', [1, 2, 0]],
    ['Q06 row', 'first row closed by the reader', ['x:scenario-background', 1, 0]],
    ['Q11 first row', 'failing second row', [1, 0]],
    ['Q11 second row', 'failing first row', [1, 0]],
    ['Q11 ruled row', 'in a rule', [1, 0]],
    ['R05 row', 'one row fails', [1, 2, 0]],
    ['R23 row', 'a hook fails for the second', [1, 2, 0]],
    ['R63 row', 'a composite fails for one row', [1, 0]],
];

(async () => {
    const report = path.resolve(process.argv[2]);
    const browser = await launchInstalled(chromium);
    for (const [outline, label, actions] of sequences) {
        const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.goto('file:///' + report.split(path.sep).join('/'));
        await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
        const log = await page.evaluate(async ([outline, actions]) => {
            const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes(outline));
            if (!scenario) return ['no scenario for ' + outline];
            for (let e = scenario; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true;
            const rowsNow = () => { const tables = [...scenario.querySelectorAll('table')].filter(t => t.querySelector('tbody tr[data-row-idx]') && t.getClientRects().length); return [...tables[0].querySelectorAll('tbody tr[data-row-idx]')]; };
            const panel = () => [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
            const read = () => {
                const p = panel();
                const out = [...p.querySelectorAll('details')].map(d => {
                    const name = d.className.replace('scenario-', '').replace('failure-result', 'failure').replace('step step-collapsible step-background', 'bgComposite').replace('step step-collapsible', 'composite');
                    const failed = d.classList.contains('step-collapsible') && d.querySelector(':scope > summary .step-status.failed') ? '(failed)' : '';
                    const holder = d.querySelector(':scope > .sub-steps');
                    const held = holder ? (holder.hasAttribute('data-steps') ? '(held)' : `(${holder.children.length} drawn)`) : '';
                    return `${name}${failed}${held}${d.hasAttribute('data-holds-failure') ? '*' : ''}=${d.open ? 'open' : 'shut'}`;
                });
                const files = p.querySelectorAll('a[href*="attachments/"], [href*="row-"]').length;
                return p.id.replace(/.*detail-/, 'row ') + ': ' + out.join(' ') + (files ? ' files:' + files : '');
            };
            const out = ['start ' + read()];
            const tick = () => new Promise(r => setTimeout(r, 120));
            for (const a of actions) {
                if (typeof a === 'number') { rowsNow()[a].click(); await tick(); out.push(`click row ${a} -> ${read()}`); }
                else if (a === 'f') { const b = scenario.querySelector('button.flatten-toggle'); if (!b) { out.push('no flatten toggle'); continue; } b.click(); await tick(); out.push(`flatten toggle -> ${read()}`); }
                else if (a.startsWith('x:')) {
                    const d = panel().querySelector('details.' + a.slice(2));
                    if (!d) { out.push(`no ${a}`); continue; }
                    d.querySelector(':scope > summary').click(); await tick(); out.push(`reader ${a} -> ${read()}`);
                } else if (a.startsWith('c:')) {
                    const d = panel().querySelectorAll('details.step-collapsible')[Number(a.slice(2))];
                    d.querySelector(':scope > summary').click(); await tick(); out.push(`reader ${a} -> ${read()}`);
                }
            }
            return out;
        }, [outline, actions]);
        console.log(`\n## ${outline}: ${label}`);
        console.log(log.join('\n'));
        if (errors.length) console.log('PAGE ERRORS: ' + errors.join(' ; '));
        await page.close();
    }
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
