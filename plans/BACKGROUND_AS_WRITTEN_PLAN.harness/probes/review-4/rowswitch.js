// node rowswitch.js <reports-dir> <scenario-title-part>
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
        if (!scenario) return { error: 'no scenario holds ' + part };
        for (let e = scenario; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true;
        const rows = [...scenario.querySelectorAll('table tbody tr[data-row-idx]')];
        const visiblePanel = () => [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
        const read = () => {
            const panel = visiblePanel();
            if (!panel) return { panel: false };
            return [...panel.querySelectorAll('details')].filter(d => !d.classList.contains('step-collapsible')).map(d =>
                `${d.className}${d.hasAttribute('data-holds-failure') ? '[holds]' : ''}=${d.open ? 'open' : 'closed'}`);
        };
        const log = [];
        log.push(['start, row 0', read()]);
        for (const index of [1, 0, 2, 1, 0]) {
            if (!rows[index]) continue;
            rows[index].click();
            log.push([`click row ${index}`, read()]);
        }
        return { rows: rows.length, log };
    }, title);
    console.log(JSON.stringify(seen, null, 1));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
