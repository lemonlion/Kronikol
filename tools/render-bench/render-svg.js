'use strict';
// Render .puml files to SVG files with a given ESM engine build (bench-real.js sibling;
// same server/page shell, but saves the produced SVG next to each input as <input>.svg).
// usage: node render-svg.js <engine-file> <file1.puml> [file2.puml ...]
const path = require('path'), http = require('http'), fs = require('fs');
const SP = __dirname;
const PW = 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const engine = process.argv[2];
const files = process.argv.slice(3);
if (!engine || files.length === 0) { console.error('usage: node render-svg.js <engine> <puml...>'); process.exit(2); }

const html = `<!doctype html><html><head></head><body><div id="out"></div>
<script src="/viz-global.js"></script>
<script type="module">import {render} from '/${path.basename(engine)}';window.__render=(lines,id)=>render(lines,id,{});window.__ready=1;</script>
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
  for (const f of files) {
    const lines = fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n').split('\n');
    const r = await page.evaluate(async ({ lines }) => {
      const out = document.getElementById('out'); out.innerHTML = '';
      let err = null;
      const done = new Promise(res => { const mo = new MutationObserver(() => { if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); } }); mo.observe(out, { childList: true, subtree: true }); });
      try { window.__render(lines, 'out'); } catch (e) { err = String(e && e.message || e); }
      if (!err) await Promise.race([done, new Promise(r => setTimeout(r, 180000))]);
      const svg = out.querySelector('svg');
      return { err: err || (svg ? null : out.textContent.slice(0, 200)), svg: svg ? svg.outerHTML : null };
    }, { lines });
    if (r.err || !r.svg) { console.error(`${path.basename(f)}: FAILED ${r.err}`); continue; }
    const outPath = f.replace(/\.puml$/i, '') + '.svg';
    fs.writeFileSync(outPath, r.svg);
    console.log(`${path.basename(f)} -> ${path.basename(outPath)} (${Math.round(r.svg.length / 1024)} KB)`);
  }
  await browser.close();
  server.close();
})();
