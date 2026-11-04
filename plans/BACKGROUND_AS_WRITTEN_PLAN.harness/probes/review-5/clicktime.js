// node clicktime.js <report.html> <outline title part>: how long selectRow takes over 100 row clicks in a big outline, and the first paint of the group opening
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));
(async () => {
    const report = path.resolve(process.argv[2]);
    const title = process.argv[3];
    const browser = await launchInstalled(chromium);
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    await page.goto('file:///' + report.split(path.sep).join('/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 100, timeout: 120000 });
    const seen = await page.evaluate(async title => {
        const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes(title));
        for (let e = scenario.parentElement; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true;
        const t0 = performance.now();
        scenario.open = true;
        await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
        const openMs = performance.now() - t0;
        const rows = [...scenario.querySelectorAll('table tbody tr[data-row-idx]')].filter(r => r.getClientRects().length);
        const times = [];
        for (let i = 0; i < 100; i++) {
            const row = rows[(i * 7 + 1) % rows.length];
            const t = performance.now();
            row.click();
            document.body.offsetHeight;
            times.push(performance.now() - t);
        }
        times.sort((a, b) => a - b);
        return { rows: rows.length, openMs: Math.round(openMs), clickMedianMs: Math.round(times[50] * 10) / 10, clickP95Ms: Math.round(times[95] * 10) / 10 };
    }, title);
    console.log(path.basename(path.dirname(report)), JSON.stringify(seen));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
