// node cost.js <big-old/big.html> <big-new/big.html> <word>: what a one-match filter search costs on an outline of many rows, and what it leaves open.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('<tree>/tools/playwright/installed-browser.js');

async function measure(browser, report, word) {
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    await page.goto('file:///' + report.split(path.sep).join('/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 120000 });
    await page.waitForTimeout(1500);
    const result = await page.evaluate(async word => {
        const frame = () => new Promise(r => requestAnimationFrame(() => r()));
        const open = () => [...document.querySelectorAll('.param-detail-panel details')].filter(d => d.open).length;
        const panels = document.querySelectorAll('.param-detail-panel').length;
        let toggles = 0;
        document.addEventListener('toggle', () => { toggles++; }, true);
        const before = open();
        const bar = document.getElementById('searchbar');
        bar.value = word;
        const t0 = performance.now();
        run_search_scenarios();
        const sync = performance.now() - t0;
        await frame(); await frame();
        const settled = performance.now() - t0;
        await new Promise(r => setTimeout(r, 1000));
        const after = open();
        const togglesSeen = toggles;
        const t1 = performance.now();
        bar.value = '';
        run_search_scenarios();
        const emptySync = performance.now() - t1;
        await frame(); await frame();
        const emptySettled = performance.now() - t1;
        const left = open();
        return { panels, before, sync: Math.round(sync), settled: Math.round(settled), after, togglesSeen, emptySync: Math.round(emptySync), emptySettled: Math.round(emptySettled), left };
    }, word);
    await page.close();
    return result;
}

(async () => {
    const [oldFile, newFile, word] = process.argv.slice(2);
    const browser = await launchInstalled(chromium);
    for (let i = 0; i < 3; i++) {
        console.log('5.28.2', JSON.stringify(await measure(browser, path.resolve(oldFile), word)));
        console.log('patch ', JSON.stringify(await measure(browser, path.resolve(newFile), word)));
    }
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
