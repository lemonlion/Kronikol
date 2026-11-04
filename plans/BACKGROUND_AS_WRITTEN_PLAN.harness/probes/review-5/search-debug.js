const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));
(async () => {
    const report = path.resolve(process.argv[2]);
    const [word] = process.argv.slice(3);
    const browser = await launchInstalled(chromium);
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    page.on('pageerror', e => console.log('PAGEERROR', e.message));
    await page.goto('file:///' + report.split(path.sep).join('/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
    const res = await page.evaluate(word => {
        const rows = [...document.querySelectorAll('tr[data-row-search]')].filter(r => r.getAttribute('data-row-search').includes(word)).map(r => r.getAttribute('data-row-search').slice(0, 200));
        const groups = [...document.querySelectorAll('details.scenario')].map(s => ({ title: s.querySelector(':scope > summary').textContent.trim().slice(0, 30), search: (s.getAttribute('data-search') || '').includes(word), dataSearchLen: (s.getAttribute('data-search') || '').length }));
        const bar = document.getElementById('searchbar');
        return { rows, groups, bar: bar ? { tag: bar.tagName, keyup: bar.getAttribute('onkeyup'), hiddenParent: !!bar.closest('[hidden]') } : null, hasFn: typeof search_scenarios };
    }, word);
    console.log(JSON.stringify(res, null, 1));
    await page.evaluate(w => { const bar = document.getElementById('searchbar'); bar.value = w; run_search_scenarios(); }, word);
    await page.waitForTimeout(2000);
    const after = await page.evaluate(() => ({
        shown: [...document.querySelectorAll('details.scenario')].filter(s => s.style.display !== 'none').map(s => s.querySelector(':scope > summary').textContent.trim().slice(0, 30) + (s.open ? ' [open]' : '')),
        marked: [...document.querySelectorAll('tr.row-search-match')].map(r => r.textContent.trim().slice(0, 30)),
        active: [...document.querySelectorAll('tr.row-active')].map(r => r.textContent.trim().slice(0, 30)),
    }));
    console.log(JSON.stringify(after, null, 1));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
