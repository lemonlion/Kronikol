// node hit.js <rows.html>: the Search tab's own lines for a word in a later row's Background, and following one
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));

(async () => {
    const report = path.resolve(process.argv[2]);
    const word = process.argv[3] || 'zebraword';
    const title = process.argv[4] || 'FirstFails';
    const url = 'file:///' + report.split(path.sep).join('/');
    const browser = await launchInstalled(chromium);
    const context = await browser.newContext({ viewport: { width: 1100, height: 800 } });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto(url + '#tab=search&search=' + encodeURIComponent(word));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
    await page.waitForTimeout(2500);
    const lines = await page.evaluate(() => [...document.querySelectorAll('.search-lines a, .search-line a, a[data-hit]')].map(a => ({ href: a.getAttribute('href'), hit: a.getAttribute('data-hit'), text: a.textContent.trim().slice(0, 60) })).slice(0, 8));
    console.log('lines', JSON.stringify(lines));
    const link = lines.find(l => l.text.toLowerCase().includes(word));
    if (link) {
        await page.evaluate(word => { [...document.querySelectorAll('.search-lines a, .search-line a, a[data-hit]')].find(a => a.textContent.toLowerCase().includes(word)).click(); }, word);
        await page.waitForTimeout(2500);
        const seen = await page.evaluate(([title, word]) => {
            const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes(title));
            const panel = [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
            const states = [...panel.querySelectorAll('details')].map(d => `${d.className.replace('scenario-', '').replace('step step-collapsible step-background', 'bgComposite').replace('step step-collapsible', 'composite')}${d.hasAttribute('data-holds-failure') ? '*' : ''}=${d.open ? 'open' : 'shut'}`).join(' ');
            const a = document.activeElement;
            return {
                shown: panel.id.replace(/.*detail-/, 'row '),
                states,
                activeRow: (scenario.querySelector('tr.row-active') || { textContent: '' }).textContent.trim().slice(0, 30),
                focus: a ? a.tagName + ' in ' + ((a.closest('[id*="-detail-"]') || {}).id || '-') + ': ' + a.textContent.trim().slice(0, 60) : null,
                focusHasBoxes: a ? a.getClientRects().length > 0 : null,
                address: location.hash.slice(0, 120),
                status: [...document.querySelectorAll('[role="status"], .kron-nav-say')].map(e => e.textContent.trim()).filter(Boolean).slice(0, 2),
            };
        }, [title, word]);
        console.log('followed', JSON.stringify(seen, null, 1));
    }
    if (errors.length) console.log('PAGE ERRORS: ' + errors.join(' ; '));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
