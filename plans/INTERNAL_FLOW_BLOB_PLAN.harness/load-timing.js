// Load cost of the segment block, measured in Chromium rather than Node.
//
//   node load-timing.js <TestRunReport.html> [runs=7]
//
// Writes two copies of the report to a temp folder: `today.html` as it is, and `after.html` with
// the `window.__iflowSegments` script replaced by the plan's `iflow-segments` element (Node zlib
// level 6, within a few percent of .NET Optimal). Loads each `runs` times in headless Chromium
// with every network request aborted (the file alone is measured), alternating, and prints the
// median navigation timings and the JS heap after load. Then decodes the blob on the `after`
// page five times, the way the popup script will, and prints the decompress and parse times.
//
// Uses the E2E project's own Playwright driver, so the E2E project must have been built once and
// its browsers installed (the toolbar harness does the same).
'use strict';
const fs = require('fs'), path = require('path'), zlib = require('zlib'), os = require('os');

const reportPath = process.argv[2];
const runs = parseInt(process.argv[3] || '7', 10);
if (!reportPath) { console.error('usage: node load-timing.js <TestRunReport.html> [runs]'); process.exit(2); }
const driver = path.resolve(__dirname, '..', '..', 'tests', 'Kronikol.Tests.EndToEnd', 'bin', 'Debug', 'net10.0', '.playwright', 'package');
const { chromium } = require(driver);

const html = fs.readFileSync(reportPath, 'latin1');            // bytes one to one
const open = /<script>window\.__iflowSegments = /.exec(html);
if (!open) throw new Error('no window.__iflowSegments block in ' + reportPath);
const blockStart = open.index + open[0].length;
const blockEnd = html.indexOf('</script>', blockStart);
const block = html.slice(blockStart, blockEnd).replace(/;\s*$/, '');
const z = zlib.gzipSync(Buffer.from(block, 'latin1'), { level: 6 }).toString('base64');
const element = '<script id="iflow-segments" type="application/json">{"hidden":[],"z":"' + z + '"}</script>';
const after = html.slice(0, open.index) + element + html.slice(blockEnd + '</script>'.length);

const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'iflow-load-timing-'));
const pages = { today: path.join(dir, 'today.html'), after: path.join(dir, 'after.html') };
fs.writeFileSync(pages.today, html, 'latin1');
fs.writeFileSync(pages.after, after, 'latin1');
console.log(`today ${html.length.toLocaleString()} bytes; after ${after.length.toLocaleString()} bytes ` +
    `(block ${block.length.toLocaleString()} -> element ${element.length.toLocaleString()}); pages in ${dir}`);

const fileUrl = p => 'file:///' + p.replace(/\\/g, '/');
const median = (arr, k) => { const v = arr.map(x => x[k]).sort((a, b) => a - b); return v[Math.floor(v.length / 2)]; };

(async () => {
    const browser = await chromium.launch({ args: ['--enable-precise-memory-info'] });
    async function load(file) {
        const ctx = await browser.newContext();
        const page = await ctx.newPage();
        await page.route(/^https?:\/\//, r => r.abort());
        const t0 = Date.now();
        await page.goto(fileUrl(file), { waitUntil: 'load' });
        const wall = Date.now() - t0;
        const nav = await page.evaluate(() => {
            const n = performance.getEntriesByType('navigation')[0];
            return {
                responseEnd: n.responseEnd, domInteractive: n.domInteractive,
                domContentLoaded: n.domContentLoadedEventStart, load: n.loadEventStart,
                heapMB: performance.memory ? performance.memory.usedJSHeapSize / 1048576 : -1,
            };
        });
        await ctx.close();
        return Object.assign(nav, { wall });
    }
    const results = { today: [], after: [] };
    for (let i = 0; i < runs; i++) {
        results.today.push(await load(pages.today));
        results.after.push(await load(pages.after));
    }
    console.log(`\nmedian of ${runs} loads, ms from navigation start (heap in MB after load):`);
    console.log('metric'.padEnd(18) + 'today'.padStart(8) + 'after'.padStart(8));
    for (const k of ['responseEnd', 'domInteractive', 'domContentLoaded', 'load', 'wall', 'heapMB']) {
        console.log(k.padEnd(18) + String(Math.round(median(results.today, k))).padStart(8) + String(Math.round(median(results.after, k))).padStart(8));
    }

    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await page.route(/^https?:\/\//, r => r.abort());
    await page.goto(fileUrl(pages.after), { waitUntil: 'load' });
    const decode = await page.evaluate(async () => {
        const el = document.getElementById('iflow-segments');
        const out = [];
        for (let i = 0; i < 5; i++) {
            const t0 = performance.now();
            const payload = JSON.parse(el.textContent);
            const text = await decompressGzipBase64(payload.z);
            const t1 = performance.now();
            const map = JSON.parse(text);
            const t2 = performance.now();
            out.push({ read: 0, decompress: t1 - t0, parse: t2 - t1, keys: Object.keys(map).length,
                heapMB: performance.memory ? performance.memory.usedJSHeapSize / 1048576 : -1 });
        }
        return out;
    });
    console.log('\ndecode on the after page, five times in one page (ms; heap after each):');
    for (const d of decode) console.log(`  decompress ${d.decompress.toFixed(1).padStart(7)}  JSON.parse ${d.parse.toFixed(1).padStart(7)}  keys ${d.keys}  heap ${d.heapMB.toFixed(0)} MB`);
    await browser.close();
    fs.rmSync(dir, { recursive: true, force: true });   // the two copies are 11 MB of temp; comment this out to keep them
})().catch(e => { console.error(e); process.exit(1); });
