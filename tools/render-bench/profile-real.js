'use strict';
// CPU-profile rendering of .puml files with a given ESM engine build (use an UNOBFUSCATED build for readable names).
// usage: node profile-real.js <engine-file> <file.puml> [more.puml ...]
// env: PRAGMA=1 inject "!pragma teoz true"; MAXSVG=n pass maxSvgSize; REPS=n profiled renders per file (default 3)
// Prints top functions by self time, aggregated across all renders (warm: one unprofiled render happens first).
const path = require('path'), http = require('http'), fs = require('fs');
const SP = __dirname;
const PW = 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const engine = process.argv[2];
const files = process.argv.slice(3);
const REPS = Number(process.env.REPS || 3);
if (!engine || files.length === 0) { console.error('usage: node profile-real.js <engine> <puml...>'); process.exit(2); }

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
  await cdp.send('Profiler.enable');
  await cdp.send('Profiler.setSamplingInterval', { interval: 100 }); // microseconds

  const agg = new Map(); // fn key -> self samples
  const aggTotal = new Map(); // fn key -> inclusive samples (each sample credits every DISTINCT fn on its stack once)
  let totalSamples = 0, totalMs = 0;
  const times = [];
  for (const f of files) {
    let lines = fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n').split('\n');
    if (process.env.PRAGMA === '1' && !lines.some(l => l.includes('!pragma teoz')))
      lines = lines.flatMap(l => l.trim() === '@startuml' ? [l, '!pragma teoz true'] : [l]);
    const warm = await renderOnce(page, lines); // warm-up, unprofiled
    let ms = 0;
    for (let rep = 0; rep < REPS; rep++) {
      await cdp.send('Profiler.start');
      const r = await renderOnce(page, lines);
      const { profile } = await cdp.send('Profiler.stop');
      ms += r.ms;
      const byId = new Map(profile.nodes.map(n => [n.id, n]));
      const parentOf = new Map();
      for (const n of profile.nodes) for (const c of (n.children || [])) parentOf.set(c, n.id);
      for (const n of profile.nodes) {
        const cf = n.callFrame;
        if (!n.hitCount) continue;
        const key = (cf.functionName || '(anonymous)');
        agg.set(key, (agg.get(key) || 0) + n.hitCount);
        totalSamples += n.hitCount;
        { // inclusive: credit every distinct function on this sample's stack once
          const seen = new Set();
          let id = n.id;
          while (id !== undefined) {
            const nn = byId.get(id);
            const nk = (nn.callFrame.functionName || '(anonymous)');
            if (!seen.has(nk)) { seen.add(nk); aggTotal.set(nk, (aggTotal.get(nk) || 0) + n.hitCount); }
            id = parentOf.get(id);
          }
        }
        if (process.env.FOCUS && key === process.env.FOCUS) {
          let pid = parentOf.get(n.id), chain = [];
          while (pid !== undefined && chain.length < 3) {
            const pn = byId.get(pid);
            const pname = pn.callFrame.functionName || '(anonymous)';
            chain.push(pname);
            pid = parentOf.get(pid);
          }
          const ckey = chain.join(' <- ');
          global.__focus = global.__focus || new Map();
          global.__focus.set(ckey, (global.__focus.get(ckey) || 0) + n.hitCount);
        }
      }
    }
    times.push({ file: path.basename(f), warmMs: warm.ms, avgProfiledMs: Math.round(ms / REPS), err: warm.err });
    totalMs += ms;
  }
  await browser.close(); server.close();
  console.log(JSON.stringify({ engine: path.basename(engine), pragma: process.env.PRAGMA === '1', reps: REPS, totalSamples }));
  console.table(times);
  const top = [...agg.entries()].sort((a, b) => b[1] - a[1]).slice(0, 40);
  for (const [fn, hits] of top)
    console.log((100 * hits / totalSamples).toFixed(1).padStart(5) + '%  ' + Math.round(hits * totalMs / REPS / totalSamples).toString().padStart(6) + 'ms  ' + fn.slice(0, 110));
  console.log('--- top by INCLUSIVE (total) time:');
  const topT = [...aggTotal.entries()].sort((a, b) => b[1] - a[1]).slice(0, Number(process.env.TOPTOTAL || 40));
  for (const [fn, hits] of topT)
    console.log((100 * hits / totalSamples).toFixed(1).padStart(5) + '%  ' + fn.slice(0, 110));
  if (global.__focus) {
    console.log('--- callers of ' + process.env.FOCUS + ' (parent chains by self hits):');
    for (const [chain, hits] of [...global.__focus.entries()].sort((a, b) => b[1] - a[1]).slice(0, 12))
      console.log('  ' + hits.toString().padStart(6) + '  ' + chain.slice(0, 160));
  }
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
