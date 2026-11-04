// The Features summary as painted, one row per feature whose name holds <filter>: node features-table.js <reports-dir> <filter> [label]
// Bundled Chromium only. The table's cells are read as text, with the column headings beside them.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.join(__dirname, '../../../tools/playwright/installed-browser.js'));
(async () => {
    const [reports, filter, label] = process.argv.slice(2);
    const browser = await launchInstalled(chromium);
    const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
    await page.goto('file:///' + path.resolve(reports, 'TestRunReport.html').replace(/\\/g, '/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
    const rows = await page.evaluate(wanted => {
        const tables = [...document.querySelectorAll('table')].filter(t => /steps?/i.test(t.textContent) && /scenarios?/i.test(t.textContent));
        const out = [];
        for (const table of tables) {
            const heads = [...table.querySelectorAll('thead tr')].map(r => [...r.cells].map(c => c.textContent.trim()));
            for (const row of table.querySelectorAll('tbody tr')) {
                const cells = [...row.cells].map(c => c.textContent.trim());
                if (cells[0] && cells[0].includes(wanted)) out.push({ heads, cells: cells.slice(0, 9) });
            }
        }
        return out;
    }, filter);
    console.log(label || reports);
    if (rows[0]) console.log('  headings: ' + JSON.stringify(rows[0].heads));
    for (const r of rows) console.log('  ' + r.cells.join(' | '));
    if (!rows.length) console.log('  no row holds ' + filter);
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
