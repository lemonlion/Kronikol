'use strict';
// CPU-profile rendering of .puml files and SAVE the raw profiles for offline slicing (see profile-analyze.js).
// usage: node profile-save.js <engine-file> <out-dir> <file.puml> [more.puml ...]
// env: PRAGMA=1 inject "!pragma teoz true"; MAXSVG=n pass maxSvgSize; REPS=n profiled renders per file (default 5);
//      INTERVAL=n sampling interval in microseconds (default 100); BROWSER=chromium (CDP profiling is Chromium-only).
// One unprofiled warm-up render per file, then REPS profiled renders, each saved as <out-dir>/<file>.<rep>.cpuprofile.
const path = require('path'), http = require('http'), fs = require('fs');
const SP = __dirname;
const PW = process.env.BENCH_PW || 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const engine = process.argv[2];
const outDir = process.argv[3];
const files = process.argv.slice(4);
const REPS = Number(process.env.REPS || 5);
if (!engine || !outDir || files.length === 0) { console.error('usage: node profile-save.js <engine> <out-dir> <puml...>'); process.exit(2); }
fs.mkdirSync(outDir, { recursive: true });

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
    return { ms: performance.now() - t0, err };
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
  await cdp.send('Profiler.setSamplingInterval', { interval: Number(process.env.INTERVAL || 100) });

  const times = [];
  for (const f of files) {
    let lines = fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n').split('\n');
    if (process.env.PRAGMA === '1' && !lines.some(l => l.includes('!pragma teoz')))
      lines = lines.flatMap(l => l.trim() === '@startuml' ? [l, '!pragma teoz true'] : [l]);
    const warm = await renderOnce(page, lines);
    const ms = [];
    for (let rep = 0; rep < REPS; rep++) {
      await cdp.send('Profiler.start');
      const r = await renderOnce(page, lines);
      const { profile } = await cdp.send('Profiler.stop');
      ms.push(r.ms);
      fs.writeFileSync(path.join(outDir, `${path.basename(f, '.puml')}.${rep}.cpuprofile`), JSON.stringify({ file: path.basename(f), renderMs: r.ms, profile }));
    }
    ms.sort((a, b) => a - b);
    times.push({ file: path.basename(f), warmupMs: Math.round(warm.ms), medianProfiledMs: Math.round(ms[ms.length >> 1]), err: warm.err });
  }
  await browser.close(); server.close();
  console.log(JSON.stringify({ engine: path.basename(engine), reps: REPS, outDir }));
  console.table(times);
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
