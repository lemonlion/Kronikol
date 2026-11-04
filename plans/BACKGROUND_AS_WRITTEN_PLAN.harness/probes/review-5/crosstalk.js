// node crosstalk.js <rows.html>: a reader's work in one outline leaves every other outline's panels as they were.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));
(async () => {
    const report = path.resolve(process.argv[2]);
    const browser = await launchInstalled(chromium);
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    await page.goto('file:///' + report.split(path.sep).join('/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
    const seen = await page.evaluate(() => {
        const groups = [...document.querySelectorAll('details.scenario-parameterized')];
        const snapshot = g => [...g.querySelectorAll('.param-detail-panel')].map(p => (p.style.display === 'none' ? '-' : '+') + [...p.querySelectorAll('details')].map(d => d.open ? 'o' : 's').join('')).join(' ');
        const pay = groups.find(g => g.querySelector(':scope > summary').textContent.includes('Pay'));
        const others = groups.filter(g => g !== pay);
        const before = others.map(snapshot);
        const rows = [...pay.querySelectorAll('table.param-test-table:not(.param-table-grouped) tbody tr[data-row-idx]')];
        pay.querySelector('.param-detail-panel details.scenario-background').open = true;
        rows[1].click(); rows[2].click(); rows[3].click(); rows[0].click();
        const after = others.map(snapshot);
        return { outlines: groups.length, same: JSON.stringify(before) === JSON.stringify(after), pay: snapshot(pay) };
    });
    console.log(JSON.stringify(seen));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
