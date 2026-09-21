'use strict';
// N-engine interleaved benchmark (same method as upstream tools/perf-bench, which caps at two engines):
// one page per engine in ONE browser, engines alternated per rep so paired samples sit next to each other in time,
// the first (cold) render per engine per file discarded, warm medians reported, SVG SHA-256 compared to engine 0.
// usage: node bench-ladder.js <engine0.js> [engine1.js ...] -- <file.puml> [more.puml ...]
// env: REPS=n warm reps per engine per file (default 8); MAXSVG=n maxSvgSize (default 98304); PRAGMA=1 inject teoz pragma;
//      BROWSER=chromium|firefox (default chromium); JSON=<path> also write every rep.
const path = require('path'), http = require('http'), fs = require('fs');
const SP = __dirname;
const PW = process.env.BENCH_PW || 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const argv = process.argv.slice(2);
const sep = argv.indexOf('--');
const engines = argv.slice(0, sep), files = argv.slice(sep + 1);
const REPS = Number(process.env.REPS || 8), MAXSVG = Number(process.env.MAXSVG || 98304);
if (sep < 1 || files.length === 0) { console.error('usage: node bench-ladder.js <engine...> -- <puml...>'); process.exit(2); }

const pageHtml = e => `<!doctype html><html><head><script>
window.__m={measureText:0};
(function(){const omt=CanvasRenderingContext2D.prototype.measureText;CanvasRenderingContext2D.prototype.measureText=function(s){window.__m.measureText++;return omt.call(this,s);};})();
</script></head><body><div id="out"></div>
<script src="/viz-global.js"></script>
<script type="module">import {render} from '/${path.basename(e)}';window.__render=(lines,id)=>render(lines,id,{maxSvgSize:${MAXSVG}});window.__ready=1;</script>
</body></html>`;

const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  const m = u.match(/^\/page(\d+)\.html$/);
  if (m) { res.setHeader('content-type', 'text/html'); return res.end(pageHtml(engines[Number(m[1])])); }
  const p = path.join(SP, u); if (!fs.existsSync(p)) { res.statusCode = 404; return res.end(); }
  res.setHeader('content-type', 'application/javascript'); res.setHeader('cache-control', 'no-store'); fs.createReadStream(p).pipe(res);
});

const renderOnce = (page, lines) => page.evaluate(async ({ lines, id }) => {
  const out = document.getElementById('out'); out.innerHTML = '';
  const m0 = window.__m.measureText, t0 = performance.now();
  let err = null;
  const done = new Promise(res => { const mo = new MutationObserver(() => { if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); } }); mo.observe(out, { childList: true, subtree: true }); });
  try { window.__render(lines, id); } catch (e) { err = String(e && e.message || e); }
  if (!err) await Promise.race([done, new Promise(r => setTimeout(r, 180000))]);
  const ms = performance.now() - t0;
  const svg = out.querySelector('svg');
  let sha = null;
  if (svg) { const buf = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(svg.outerHTML)); sha = [...new Uint8Array(buf)].slice(0, 8).map(b => b.toString(16).padStart(2, '0')).join(''); }
  return { ms, err: err || (svg ? null : out.textContent.slice(0, 90)), measureText: window.__m.measureText - m0, sha };
}, { lines, id: 'out' });

const median = a => { const s = [...a].sort((x, y) => x - y); const n = s.length; return n % 2 ? s[n >> 1] : (s[n / 2 - 1] + s[n / 2]) / 2; };

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browserName = process.env.BROWSER || 'chromium';
  const browser = await pw[browserName].launch({ headless: true });
  const pages = [];
  for (let i = 0; i < engines.length; i++) {
    const page = await browser.newPage();
    page.on('pageerror', e => console.error('PAGEERROR[' + i + ']', e.message));
    await page.goto(`http://127.0.0.1:${port}/page${i}.html`, { waitUntil: 'load' });
    await page.waitForFunction('window.__ready && window.__render', null, { timeout: 120000 });
    pages.push(page);
  }
  const all = [];
  const rows = [];
  for (const f of files) {
    let lines = fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n').split('\n');
    if (process.env.PRAGMA === '1' && !lines.some(l => l.includes('!pragma teoz')))
      lines = lines.flatMap(l => l.trim() === '@startuml' ? [l, '!pragma teoz true'] : [l]);
    const samples = engines.map(() => []); const shas = engines.map(() => null); const errs = engines.map(() => null); const mt = engines.map(() => 0);
    for (let i = 0; i < engines.length; i++) { const r = await renderOnce(pages[i], lines); errs[i] = r.err; } // cold, discarded
    for (let rep = 0; rep < REPS; rep++)
      for (let k = 0; k < engines.length; k++) {
        const i = (k + rep) % engines.length; // rotate the starting engine each rep
        const r = await renderOnce(pages[i], lines);
        samples[i].push(r.ms); shas[i] = r.sha; mt[i] = r.measureText; errs[i] = errs[i] || r.err;
        all.push({ file: path.basename(f), engine: path.basename(engines[i]), rep, ms: r.ms });
      }
    const base = median(samples[0]);
    const row = { file: path.basename(f, '.puml') };
    engines.forEach((e, i) => {
      const med = median(samples[i]);
      row['e' + i + ' ms'] = errs[i] ? 'ERR' : Math.round(med);
      if (i > 0) { row['e' + i + '/e0'] = (med / base).toFixed(2); row['e' + i + ' svg'] = shas[i] === shas[0] ? 'same' : 'DIFF'; }
    });
    row.measureText = mt.join('/');
    rows.push(row);
  }
  const version = browser.version();
  await browser.close(); server.close();
  engines.forEach((e, i) => console.log('e' + i + ' = ' + path.basename(e)));
  console.log(`${browserName} ${version}, REPS ${REPS} warm (cold discarded), engines interleaved, maxSvgSize ${MAXSVG}`);
  console.table(rows);
  // geometric mean of the ratios per engine
  for (let i = 1; i < engines.length; i++) {
    const rs = rows.map(r => Number(r['e' + i + '/e0'])).filter(x => x > 0);
    console.log('e' + i + ' geomean ratio vs e0: ' + Math.exp(rs.reduce((a, x) => a + Math.log(x), 0) / rs.length).toFixed(3) + '   all svg same: ' + rows.every(r => r['e' + i + ' svg'] === 'same'));
  }
  if (process.env.JSON) fs.writeFileSync(process.env.JSON, JSON.stringify({ engines, browser: browserName, version, reps: REPS, all }, null, 1));
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
