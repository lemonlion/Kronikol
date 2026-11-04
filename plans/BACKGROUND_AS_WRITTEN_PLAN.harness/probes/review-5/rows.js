// node rows.js <report.html>: click sequences over the outlines of gen's rows.html, printing each panel's non-step <details> after each.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));

const sequences = [
    // [outline, label, actions]: a number clicks that row; 'x:<class>' clicks the summary of that section in the row shown
    ['Pay', 'no reader', [1, 0, 2, 3, 0]],
    ['Pay', 'reader opens a, goes through the failed row to c and back', ['x:scenario-background', 1, 2, 0]],
    ['Pay', 'reader opens a, straight to c', ['x:scenario-background', 2, 0]],
    ['Pay', 'reader closes b, on to d, a, back to b', [1, 'x:scenario-background', 3, 0, 1]],
    ['Pay', 'reader closes Steps on a, through the failed row', ['x:scenario-steps', 1, 2]],
    ['Pay', 'reader opens c, closes c, to b, back to c', [2, 'x:scenario-background', 'x:scenario-background', 1, 2]],
    ['Mixed', 'each row in order', [1, 2, 3, 4, 5, 0]],
    ['Mixed', 'reader closes a Failure Result', [1, 'x:failure-result', 2, 3, 4, 5, 0]],
    ['Mixed', 'reader closes Steps on a', ['x:scenario-steps', 1, 2, 3, 4, 5]],
    ['Mixed', 'reader opens the Background on a', ['x:scenario-background', 1, 2, 3, 4, 5]],
    ['Composite', 'each row', [1, 2, 3, 0]],
    ['Composite', 'reader opens a composite on a', ['c:0', 1, 2, 3]],
    ['InARule', 'a to b and back', [1, 0]],
    ['FirstFails', 'the first row fails in a composite by itself', [1, 2, 0]],
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
            for (let e = scenario; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true;
            const tables = [...scenario.querySelectorAll('table')].filter(t => t.querySelector('tbody tr[data-row-idx]') && t.getClientRects().length);
            const rows = [...tables[0].querySelectorAll('tbody tr[data-row-idx]')];
            const panel = () => [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
            const read = () => {
                const p = panel();
                const out = [...p.querySelectorAll('details')].map(d => {
                    const name = d.className.replace('scenario-', '').replace('failure-result', 'failure').replace('step step-collapsible step-background', 'bgComposite').replace('step step-collapsible', 'composite');
                    return `${name}${d.hasAttribute('data-holds-failure') ? '*' : ''}=${d.open ? 'open' : 'shut'}`;
                });
                return p.id.replace(/.*detail-/, 'row ') + ': ' + out.join(' ');
            };
            const out = ['start ' + read()];
            const tick = () => new Promise(r => setTimeout(r, 60));
            for (const a of actions) {
                if (typeof a === 'number') { rows[a].click(); await tick(); out.push(`click row ${a} -> ${read()}`); }
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
