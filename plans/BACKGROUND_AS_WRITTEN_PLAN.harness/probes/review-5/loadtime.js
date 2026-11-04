// node loadtime.js <report.html> [runs]: time to the first feature, element count, and time to open every scenario; the medians
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.resolve(__dirname, '../../../../tools/playwright/installed-browser.js'));
(async () => {
    const report = path.resolve(process.argv[2]);
    const runs = Number(process.argv[3] || 5);
    const browser = await launchInstalled(chromium);
    const times = [], opens = [];
    let elements = 0, details = 0, held = 0, heap = 0;
    for (let i = 0; i < runs; i++) {
        const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
        const start = Date.now();
        await page.goto('file:///' + report.split(path.sep).join('/'));
        await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 50, timeout: 180000 });
        times.push(Date.now() - start);
        const r = await page.evaluate(async () => {
            const count = document.querySelectorAll('*').length;
            const t0 = performance.now();
            document.querySelectorAll('details.feature').forEach(d => d.open = true);
            document.querySelectorAll('details.scenario').forEach(d => d.open = true);
            await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
            const open = performance.now() - t0;
            return {
                count, open,
                openDetails: document.querySelectorAll('details.step-collapsible[open]').length,
                held: document.querySelectorAll('.sub-steps[data-steps]').length,
                heap: performance.memory ? performance.memory.usedJSHeapSize : 0,
            };
        });
        elements = r.count; details = r.openDetails; held = r.held; heap = r.heap; opens.push(Math.round(r.open));
        await page.close();
    }
    const med = a => [...a].sort((x, y) => x - y)[Math.floor(a.length / 2)];
    console.log(JSON.stringify({ report: path.basename(path.dirname(report)) + '/' + path.basename(report), loadMs: med(times), openAllMs: med(opens), loadRuns: times, elements, openComposites: details, heldHolders: held, heapMB: Math.round(heap / 1048576) }));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
