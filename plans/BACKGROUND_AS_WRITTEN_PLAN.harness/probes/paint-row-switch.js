// With SeparateBackgroundSteps, an outline whose second row fails in its Background: is the section open once that row is selected? node paint-row-switch.js <reports-dir> <scenario-title-part>
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.join(__dirname, '../../../tools/playwright/installed-browser.js'));
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
            const section = panel && panel.querySelector('details.scenario-background');
            return section ? { open: section.open, holdsFailedStep: !!section.querySelector('.step-status.failed') } : { section: false };
        };
        const states = { rows: rows.length, firstRowSelected: read() };
        const failing = rows.find(r => r.classList.toString().includes('fail') || r.textContent.trim().startsWith('b')) || rows[rows.length - 1];
        failing.click();
        states.failingRowAfterTheClick = read();
        rows[0].click();
        states.backOnTheFirstRow = read();
        return states;
    }, title);
    console.log(JSON.stringify(seen, null, 1));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
