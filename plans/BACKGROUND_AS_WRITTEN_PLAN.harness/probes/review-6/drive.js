// node drive.js <pages-old> <pages-new> <file.html> [sequence filter]: click sequences on both builds, side by side; prints a line where they differ.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));

const sequences = require(path.resolve(process.argv[5] || path.join(__dirname, 'sequences.js')));
const filter = process.argv[6] || '';

async function run(browser, report, outline, actions) {
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto('file:///' + report.split(path.sep).join('/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
    const log = await page.evaluate(async ([outline, actions]) => {
        const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes(outline));
        for (let e = scenario; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true;
        const tick = (ms = 80) => new Promise(r => setTimeout(r, ms));
        await tick();
        const rows = () => { const tables = [...scenario.querySelectorAll('table')].filter(t => t.querySelector('tbody tr[data-row-idx]') && t.getClientRects().length); return [...tables[0].querySelectorAll('tbody tr[data-row-idx]')]; };
        const panel = () => [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
        const pathOf = d => selectRowStepPath(d, panel());
        const read = () => {
            const p = panel();
            if (!p) return 'no panel';
            const out = [...p.querySelectorAll('details')].map(d => {
                const composite = d.classList.contains('step-collapsible');
                const name = composite ? 'c' + pathOf(d) : d.className.replace('scenario-', '').replace('failure-result', 'failure');
                const holder = composite ? d.querySelector(':scope > .sub-steps') : null;
                const undrawn = holder && holder.hasAttribute('data-steps') ? '~' : '';
                const failed = composite && d.querySelector('.step-status.failed') ? '!' : '';
                return `${name}${d.hasAttribute('data-holds-failure') ? '*' : ''}${failed}${undrawn}=${d.open ? 'O' : '-'}`;
            });
            return p.id.replace(/.*detail-/, 'r') + ' ' + out.join(' ');
        };
        const out = ['start: ' + read()];
        for (const a of actions) {
            if (typeof a === 'number') { rows()[a].click(); await tick(); out.push(`row ${a}: ${read()}`); continue; }
            const [verb, ...rest] = a.split(':');
            const arg = rest.join(':');
            if (verb === 's') {
                const d = panel().querySelector('details.' + arg);
                if (!d) { out.push(`no ${a}`); continue; }
                d.querySelector(':scope > summary').click(); await tick(); out.push(`${a}: ${read()}`);
            } else if (verb === 'c') {
                const d = [...panel().querySelectorAll('details.step-collapsible')].find(x => pathOf(x) === arg);
                if (!d) { out.push(`no ${a}`); continue; }
                d.querySelector(':scope > summary').click(); await tick(300); out.push(`${a}: ${read()}`);
            } else if (verb === 'q') {
                const bar = document.getElementById('searchbar');
                bar.value = arg;
                run_search_scenarios();
                await tick(700);
                out.push(`search "${arg}": ${read()} [active ${[...scenario.querySelectorAll('tr.row-active')].filter(r => r.getClientRects().length).map(r => r.getAttribute('data-row-idx')).join()}] scenario ${scenario.open ? 'open' : 'closed'}`);
            } else if (verb === 'eval') {
                out.push(`${a}: ` + String(eval(arg)));
                await tick();
            } else if (verb === 'read') {
                out.push(`read: ${read()}`);
            } else if (verb === 'all') {
                // what every panel holds
                out.push('all: ' + [...scenario.querySelectorAll('[id*="-detail-"]')].map(p => p.id.replace(/.*detail-/, 'r') + '(' + [...p.querySelectorAll('details')].filter(d => !d.classList.contains('step-collapsible')).map(d => d.classList[0].replace('scenario-', '') + (d.open ? '=O' : '=-')).join(' ') + ')').join(' '));
            }
        }
        return out;
    }, [outline, actions]);
    await page.close();
    if (errors.length) log.push('PAGE ERRORS: ' + errors.join(' ; '));
    return log;
}

(async () => {
    const [oldDir, newDir, file] = process.argv.slice(2);
    const browser = await launchInstalled(chromium);
    for (const [outline, label, actions, onlyFile] of sequences) {
        if (filter && !label.includes(filter) && !outline.includes(filter)) continue;
        if (onlyFile && onlyFile !== file) continue;
        const a = await run(browser, path.resolve(oldDir, file), outline, actions);
        const b = await run(browser, path.resolve(newDir, file), outline, actions);
        console.log(`\n## ${outline}: ${label} (${file})`);
        for (let i = 0; i < Math.max(a.length, b.length); i++) {
            if (a[i] === b[i]) console.log('   same ' + a[i]);
            else { console.log(' 5.28.2 ' + a[i]); console.log('  patch ' + b[i]); }
        }
    }
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
