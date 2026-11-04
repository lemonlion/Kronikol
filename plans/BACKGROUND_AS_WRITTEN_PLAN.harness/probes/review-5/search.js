// node search.js <report.html> <outline> <word>: the filter box's search for a word, then what shows in the row it chose; then the search emptied.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));

(async () => {
    const report = path.resolve(process.argv[2]);
    const [outline, word] = process.argv.slice(3);
    const browser = await launchInstalled(chromium);
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto('file:///' + report.split(path.sep).join('/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
    const read = () => page.evaluate(outline => {
        const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.querySelector(':scope > summary').textContent.includes(outline));
        const panel = [...scenario.querySelectorAll('[id*="-detail-"]')].find(p => p.style.display !== 'none');
        const states = [...panel.querySelectorAll('details')].map(d => `${d.className.replace('scenario-', '').replace('step step-collapsible step-background', 'bgComposite')}${d.hasAttribute('data-holds-failure') ? '*' : ''}=${d.open ? 'open' : 'shut'}`);
        const match = [...panel.querySelectorAll('.step')].find(s => s.textContent.toLowerCase().includes(window.__word || ''));
        const visible = el => { for (let e = el; e; e = e.parentElement) { if (e.style && e.style.display === 'none') return false; const up = e.parentElement; if (up && up.tagName === 'DETAILS' && !up.open && e.tagName !== 'SUMMARY') return false; } return true; };
        return {
            scenarioOpen: scenario.open,
            shown: panel.id.replace(/.*detail-/, 'row '),
            states: states.join(' '),
            activeRow: (scenario.querySelector('tr.row-active') || { textContent: '' }).textContent.trim().replace(/\s+/g, ' ').slice(0, 40),
            matchHasBoxes: match ? match.getClientRects().length > 0 : null,
            matchVisible: match ? visible(match) : null,
        };
    }, outline);
    await page.evaluate(w => { window.__word = w.toLowerCase(); }, word);
    console.log('before', JSON.stringify(await read()));
    await page.evaluate(w => {
        const bar = document.getElementById('searchbar');
        bar.value = w;
        run_search_scenarios();
    }, word);
    await page.waitForTimeout(1500);
    console.log('searched', JSON.stringify(await read()));
    await page.evaluate(() => {
        const bar = document.getElementById('searchbar');
        bar.value = '';
        run_search_scenarios();
    });
    await page.waitForTimeout(800);
    console.log('emptied', JSON.stringify(await read()));
    if (errors.length) console.log('PAGE ERRORS: ' + errors.join(' ; '));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
