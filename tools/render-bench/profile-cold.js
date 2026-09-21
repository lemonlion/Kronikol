'use strict';
// COLD-start measurement + CPU profile: a fresh page per sample, so every sample pays module evaluation and the
// first render (class initialisers, default skin parsing, JIT warm-up) exactly as a one-diagram page view does.
// usage: node profile-cold.js <engine-file> <out-dir> <file.puml>
// env: REPS=n fresh pages (default 5); MAXSVG=n; PROFILE=0 to only time (no profiler overhead).
// Saves <out-dir>/<file>.cold<rep>.cpuprofile (first render only; same format profile-analyze.js reads).
const path = require('path'), http = require('http'), fs = require('fs');
const SP = __dirname;
const PW = process.env.BENCH_PW || 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const [engine, outDir, file] = process.argv.slice(2);
const REPS = Number(process.env.REPS || 5);
const PROFILE = process.env.PROFILE !== '0';
if (!engine || !outDir || !file) { console.error('usage: node profile-cold.js <engine> <out-dir> <file.puml>'); process.exit(2); }
fs.mkdirSync(outDir, { recursive: true });

const html = `<!doctype html><html><head></head><body><div id="out"></div>
<script src="/viz-global.js"></script>
<script type="module">const t0=performance.now();const m=await import('/${path.basename(engine)}');window.__importMs=performance.now()-t0;window.__render=(lines,id)=>m.render(lines,id,${process.env.MAXSVG ? `{maxSvgSize:${Number(process.env.MAXSVG)}}` : '{}'});window.__ready=1;</script>
</body></html>`;

const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  if (u === '/index.html') { res.setHeader('content-type', 'text/html'); return res.end(html); }
  const p = path.join(SP, u); if (!fs.existsSync(p)) { res.statusCode = 404; return res.end(); }
  res.setHeader('content-type', 'application/javascript'); res.setHeader('cache-control', 'no-store'); fs.createReadStream(p).pipe(res);
});

const renderOnce = (page, lines) => page.evaluate(async ({ lines, id }) => {
  const out = document.getElementById('out'); out.innerHTML = '';
  const t0 = performance.now();
  let err = null;
  const done = new Promise(res => { const mo = new MutationObserver(() => { if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); } }); mo.observe(out, { childList: true, subtree: true }); });
  try { window.__render(lines, id); } catch (e) { err = String(e && e.message || e); }
  if (!err) await Promise.race([done, new Promise(r => setTimeout(r, 180000))]);
  return { ms: performance.now() - t0, err };
}, { lines, id: 'out' });

const median = a => { const s = [...a].sort((x, y) => x - y); return s[s.length >> 1]; };

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await pw.chromium.launch({ headless: true });
  const lines = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n').split('\n');
  const imp = [], first = [], second = [], third = [];
  for (let rep = 0; rep < REPS; rep++) {
    const context = await browser.newContext(); // fresh context: no code cache carried between samples
    const page = await context.newPage();
    page.on('pageerror', e => console.error('PAGEERROR', e.message));
    await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
    await page.waitForFunction('window.__ready && window.__render', null, { timeout: 120000 });
    imp.push(await page.evaluate(() => window.__importMs));
    let cdp = null;
    if (PROFILE) { cdp = await context.newCDPSession(page); await cdp.send('Profiler.enable'); await cdp.send('Profiler.setSamplingInterval', { interval: 100 }); await cdp.send('Profiler.start'); }
    const r1 = await renderOnce(page, lines);
    if (PROFILE) { const { profile } = await cdp.send('Profiler.stop'); fs.writeFileSync(path.join(outDir, `${path.basename(file, '.puml')}.cold${rep}.cpuprofile`), JSON.stringify({ file: path.basename(file), renderMs: r1.ms, profile })); }
    const r2 = await renderOnce(page, lines);
    const r3 = await renderOnce(page, lines);
    first.push(r1.ms); second.push(r2.ms); third.push(r3.ms);
    await context.close();
  }
  await browser.close(); server.close();
  console.log(JSON.stringify({ engine: path.basename(engine), file: path.basename(file), reps: REPS, profiled: PROFILE,
    importMs: Math.round(median(imp)), firstRenderMs: Math.round(median(first)), secondRenderMs: Math.round(median(second)), thirdRenderMs: Math.round(median(third)) }));
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
