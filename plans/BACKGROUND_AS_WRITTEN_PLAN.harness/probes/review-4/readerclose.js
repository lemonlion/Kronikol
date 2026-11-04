// node readerclose.js <reports-dir> <scenario-title-part>: the reader closes a row's failing Background section, then leaves the row and comes back.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));
(async () => {
    const [reports, title] = process.argv.slice(2);
    const browser = await launchInstalled(chromium);
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    await page.goto('file:///' + path.resolve(reports, 'TestRunReport.html').replace(/\\/g, '/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 30000 });
    const seen = await page.evaluate(part => {
        const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes(part));
        for (let e = scenario; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true;
        const rows = [...scenario.querySelectorAll('table tbody tr[data-row-idx]')];
        const visible = () => [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
        const section = () => visible().querySelector('details.scenario-background');
        const log = [];
        rows[1].click();
        log.push(['row b selected', section().open]);
        section().querySelector(':scope > summary').click();
        log.push(['reader closed it', section().open]);
        rows[0].click();
        log.push(['row a selected', section().open]);
        rows[1].click();
        log.push(['back on row b', section().open]);
        return log;
    }, title);
    console.log(JSON.stringify(seen));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
