'use strict';
// Allocation-profile rendering of .puml files (CDP HeapProfiler.startSampling) with a given ESM
// engine build. Use an UNOBFUSCATED build for readable names.
// usage: node alloc-real.js <engine-file> <file.puml> [more.puml ...]
// env: PRAGMA=1 inject "!pragma teoz true"; MAXSVG=n pass maxSvgSize; REPS=n sampled renders per
//      file (default 2); INTERVAL=n sampling interval in bytes (default 16384);
//      FOCUS=<fn> print top caller chains for that function's allocations.
// Ranks functions by sampled self-allocated bytes across all renders (one unsampled warm-up first).
const path = require('path'), http = require('http'), fs = require('fs');
const SP = __dirname;
const PW = 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const engine = process.argv[2];
const files = process.argv.slice(3);
const REPS = Number(process.env.REPS || 2);
const INTERVAL = Number(process.env.INTERVAL || 16384);
if (!engine || files.length === 0) { console.error('usage: node alloc-real.js <engine> <puml...>'); process.exit(2); }

const html = `<!doctype html><html><head></head><body><div id="out"></div>
<script src="/viz-global.js"></script>
<script type="module">import {render} from '/${path.basename(engine)}';window.__render=(lines,id)=>render(lines,id,${process.env.MAXSVG ? `{maxSvgSize:${Number(process.env.MAXSVG)}}` : '{}'});window.__ready=1;</script>
</body></html>`;

const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  if (u === '/index.html') { res.setHeader('content-type', 'text/html'); return res.end(html); }
  const p = path.join(SP, u); if (!fs.existsSync(p)) { res.statusCode = 404; return res.end(); }
  res.setHeader('content-type', 'application/javascript'); res.setHeader('cache-control', 'no-store'); fs.createReadStream(p).pipe(res);
});

async function renderOnce(page, lines) {
  return page.evaluate(async ({ lines, id }) => {
    const out = document.getElementById('out'); out.innerHTML = '';
    const t0 = performance.now();
    let err = null;
    const done = new Promise(res => { const mo = new MutationObserver(() => { if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); } }); mo.observe(out, { childList: true, subtree: true }); });
    try { window.__render(lines, id); } catch (e) { err = String(e && e.message || e); }
    if (!err) await Promise.race([done, new Promise(r => setTimeout(r, 180000))]);
    return { ms: Math.round(performance.now() - t0), err };
  }, { lines, id: 'out' });
}

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  page.on('pageerror', e => console.error('PAGEERROR', e.message));
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction('window.__ready && window.__render', null, { timeout: 120000 });
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('HeapProfiler.enable');

  const selfBytes = new Map(); // fn -> sampled self-allocated bytes
  const focus = new Map(); // caller chain -> bytes (when FOCUS set)
  let grandTotal = 0;
  const times = [];
  for (const f of files) {
    let lines = fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n').split('\n');
    if (process.env.PRAGMA === '1' && !lines.some(l => l.includes('!pragma teoz')))
      lines = lines.flatMap(l => l.trim() === '@startuml' ? [l, '!pragma teoz true'] : [l]);
    const warm = await renderOnce(page, lines); // warm-up, unsampled
    let ms = 0, fileBytes = 0;
    for (let rep = 0; rep < REPS; rep++) {
      await cdp.send('HeapProfiler.startSampling', { samplingInterval: INTERVAL });
      const r = await renderOnce(page, lines);
      const { profile } = await cdp.send('HeapProfiler.stopSampling');
      ms += r.ms;
      // profile.head is a tree: {callFrame, selfSize, children}
      const walk = (node, chain) => {
        const name = node.callFrame.functionName || '(anonymous)';
        if (node.selfSize > 0) {
          selfBytes.set(name, (selfBytes.get(name) || 0) + node.selfSize);
          grandTotal += node.selfSize; fileBytes += node.selfSize;
          if (process.env.FOCUS && name === process.env.FOCUS) {
            const ckey = chain.slice(-3).reverse().join(' <- ');
            focus.set(ckey, (focus.get(ckey) || 0) + node.selfSize);
          }
        }
        chain.push(name);
        for (const c of (node.children || [])) walk(c, chain);
        chain.pop();
      };
      walk(profile.head, []);
    }
    times.push({ file: path.basename(f), warmMs: warm.ms, avgSampledMs: Math.round(ms / REPS), sampledMB: (fileBytes / REPS / 1048576).toFixed(1), err: warm.err });
  }
  await browser.close(); server.close();
  console.log(JSON.stringify({ engine: path.basename(engine), pragma: process.env.PRAGMA === '1', reps: REPS, intervalBytes: INTERVAL, totalSampledMB: (grandTotal / 1048576).toFixed(1) }));
  console.table(times);
  const top = [...selfBytes.entries()].sort((a, b) => b[1] - a[1]).slice(0, 40);
  for (const [fn, bytes] of top)
    console.log((100 * bytes / grandTotal).toFixed(1).padStart(5) + '%  ' + (bytes / REPS / 1048576).toFixed(2).padStart(8) + 'MB/render  ' + fn.slice(0, 100));
  if (focus.size) {
    console.log('--- allocating caller chains of ' + process.env.FOCUS + ':');
    for (const [chain, bytes] of [...focus.entries()].sort((a, b) => b[1] - a[1]).slice(0, 12))
      console.log('  ' + (bytes / 1048576).toFixed(2).padStart(8) + 'MB  ' + chain.slice(0, 160));
  }
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
