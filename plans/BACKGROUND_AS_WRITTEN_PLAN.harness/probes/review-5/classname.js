// node classname.js <rows.html>: a <details> added to a row panel by a suite's own script, with a class that is a member of Object.prototype. Does the row switch still work?
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));
(async () => {
    const report = path.resolve(process.argv[2]);
    const browser = await launchInstalled(chromium);
    for (const cls of ['constructor', 'toString', 'plain-class']) {
        const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.goto('file:///' + report.split(path.sep).join('/'));
        await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
        const seen = await page.evaluate(async cls => {
            const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes('Pay'));
            for (let e = scenario; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true;
            const table = [...scenario.querySelectorAll('table')].find(t => t.querySelector('tbody tr[data-row-idx]') && t.getClientRects().length);
            const rows = [...table.querySelectorAll('tbody tr[data-row-idx]')];
            const panel = () => [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
            const before = panel().id.replace(/.*detail-/, 'row ');
            const d = document.createElement('details');
            d.className = cls;
            d.innerHTML = '<summary>added by a suite</summary>';
            panel().appendChild(d);
            rows[2].click();
            await new Promise(r => setTimeout(r, 100));
            return { before, after: panel().id.replace(/.*detail-/, 'row '), active: rows.findIndex(r => r.classList.contains('row-active')) };
        }, cls);
        console.log(cls, JSON.stringify(seen), errors.length ? 'PAGE ERROR: ' + errors.join(' ; ') : 'no page error');
        await page.close();
    }
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
