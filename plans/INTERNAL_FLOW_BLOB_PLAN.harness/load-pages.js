// load-timing.js for pages already built: time any number of report variants side by side.
//
//   node load-pages.js [runs=7] <page.html> [more pages]
//
// For the variants variants.py --write and prototype-s1.py produce. Loads each page `runs` times in
// headless Chromium, round-robin, every network request aborted (the file alone is measured, so no
// diagram renders), and prints the median navigation timings and the JS heap after load.
'use strict';
const path = require('path');

const args = process.argv.slice(2);
const runs = /^\d+$/.test(args[0] || '') ? parseInt(args.shift(), 10) : 7;
if (args.length === 0) { console.error('usage: node load-pages.js [runs] <page.html> [more]'); process.exit(2); }
const driver = path.resolve(__dirname, '..', '..', 'tests', 'Kronikol.Tests.EndToEnd', 'bin', 'Debug', 'net10.0', '.playwright', 'package');
const { chromium } = require(driver);
const fileUrl = p => 'file:///' + path.resolve(p).replace(/\\/g, '/');
const median = (arr, k) => { const v = arr.map(x => x[k]).sort((a, b) => a - b); return v[Math.floor(v.length / 2)]; };

(async () => {
    const browser = await chromium.launch({ args: ['--enable-precise-memory-info'] });
    async function load(file) {
        const ctx = await browser.newContext();
        const page = await ctx.newPage();
        await page.route(/^https?:\/\//, r => r.abort());
        await page.goto(fileUrl(file), { waitUntil: 'load' });
        const nav = await page.evaluate(() => {
            const n = performance.getEntriesByType('navigation')[0];
            return {
                responseEnd: n.responseEnd, domInteractive: n.domInteractive,
                domContentLoaded: n.domContentLoadedEventStart, load: n.loadEventStart,
                heapMB: performance.memory ? performance.memory.usedJSHeapSize / 1048576 : -1,
            };
        });
        await ctx.close();
        return nav;
    }
    const results = args.map(() => []);
    for (let i = 0; i < runs; i++)
        for (let j = 0; j < args.length; j++) results[j].push(await load(args[j]));
    const names = args.map(a => path.basename(a, '.html'));
    const w = Math.max(10, ...names.map(n => n.length + 2));
    console.log(`median of ${runs} loads, ms from navigation start (heap in MB after load):`);
    console.log('metric'.padEnd(18) + names.map(n => n.padStart(w)).join(''));
    for (const k of ['responseEnd', 'domInteractive', 'domContentLoaded', 'load', 'heapMB'])
        console.log(k.padEnd(18) + results.map(r => String(Math.round(median(r, k))).padStart(w)).join(''));
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
