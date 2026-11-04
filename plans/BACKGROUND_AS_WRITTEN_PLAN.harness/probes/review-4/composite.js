const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));
(async () => {
    const [reports, title] = process.argv.slice(2);
    const browser = await launchInstalled(chromium);
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    await page.goto('file:///' + path.resolve(reports, 'TestRunReport.html').split(path.sep).join('/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 30000 });
    const seen = await page.evaluate(part => {
        const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes(part));
        for (let e = scenario; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true;
        const rows = [...scenario.querySelectorAll('table tbody tr[data-row-idx]')];
        const panels = () => [...scenario.querySelectorAll('[id*="-detail-"]')];
        const visible = () => panels().find(p => p.style.display !== 'none');
        const read = () => { const p = visible(); const s = p.querySelector('details.scenario-background'); const c = s && s.querySelector('details.step-collapsible'); return { section: s && s.open, composite: c && c.open, compositeFailed: c && !!c.querySelector(':scope > summary .step-status.failed') }; };
        const markup = panels().map(p => { const s = p.querySelector('details.scenario-background'); const c = s && s.querySelector('details.step-collapsible'); return { section: s && s.hasAttribute('open'), composite: c && c.hasAttribute('open') }; });
        const log = [['markup of each row', markup]];
        rows[1].click();
        log.push(['row b selected from row a', read()]);
        return log;
    }, title);
    console.log(JSON.stringify(seen));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
