'use strict';
// Render arbitrary .puml files in real Chromium with a given ESM engine build.
// usage: node bench-real.js <engine-file> <file1.puml> [file2.puml ...]
// env: PRAGMA=1  -> inject "!pragma teoz true" after @startuml (mirrors Kronikol's PlantUmlCreator prefix)
//      REPS=n    -> renders per file (default 2; rep 0 is cold, later reps are warm)
const path = require('path'), http = require('http'), fs = require('fs');
const SP = __dirname;
const PW = 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const engine = process.argv[2];
const files = process.argv.slice(3);
const REPS = Number(process.env.REPS || 2);
if (!engine || files.length === 0) { console.error('usage: node bench-real.js <engine> <puml...>'); process.exit(2); }

const html = `<!doctype html><html><head><script>
window.__m={measureText:0};
(function(){const omt=CanvasRenderingContext2D.prototype.measureText;CanvasRenderingContext2D.prototype.measureText=function(s){window.__m.measureText++;return omt.call(this,s);};})();
</script></head><body><div id="out"></div>
<script src="/viz-global.js"></script>
<script type="module">import {render} from '/${path.basename(engine)}';window.__render=(lines,id)=>render(lines,id,${process.env.MAXSVG ? `{maxSvgSize:${Number(process.env.MAXSVG)}}` : '{}'});window.__ready=1;</script>
</body></html>`;

const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  if (u === '/index.html') { res.setHeader('content-type', 'text/html'); return res.end(html); }
  const p = path.join(SP, u); if (!fs.existsSync(p)) { res.statusCode = 404; return res.end(); }
  res.setHeader('content-type', 'application/javascript'); res.setHeader('cache-control', 'no-store'); fs.createReadStream(p).pipe(res);
});

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  page.on('pageerror', e => console.error('PAGEERROR', e.message));
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction('window.__ready && window.__render', null, { timeout: 120000 });
  if (process.env.SVGHASH === '1') await page.evaluate(() => { window.__wantSha = true; });
  const rows = [];
  for (const f of files) {
    let lines = fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n').split('\n');
    if (process.env.PRAGMA === '1' && !lines.some(l => l.includes('!pragma teoz')))
      lines = lines.flatMap(l => l.trim() === '@startuml' ? [l, '!pragma teoz true'] : [l]);
    for (let rep = 0; rep < REPS; rep++) {
      const r = await page.evaluate(async ({ lines, id }) => {
        const out = document.getElementById('out'); out.innerHTML = '';
        const m0 = window.__m.measureText;
        const t0 = performance.now();
        let err = null;
        const done = new Promise(res => { const mo = new MutationObserver(() => { if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); } }); mo.observe(out, { childList: true, subtree: true }); });
        try { window.__render(lines, id); } catch (e) { err = String(e && e.message || e); }
        if (!err) await Promise.race([done, new Promise(r => setTimeout(r, 180000))]);
        const svg = out.querySelector('svg');
        let sha = null;
        if (svg && window.__wantSha) {
          const buf = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(svg.outerHTML));
          sha = [...new Uint8Array(buf)].slice(0, 8).map(b => b.toString(16).padStart(2, '0')).join('');
        }
        return { ms: Math.round(performance.now() - t0), err: err || (svg ? null : out.textContent.slice(0, 90)), svgKB: svg ? Math.round(svg.outerHTML.length / 1024) : 0, measureText: window.__m.measureText - m0, sha };
      }, { lines, id: 'out' });
      rows.push({ file: path.basename(f), rep, ...r });
    }
  }
  await browser.close(); server.close();
  console.log(JSON.stringify({ engine: path.basename(engine), pragma: process.env.PRAGMA === '1' }));
  console.table(rows);
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
