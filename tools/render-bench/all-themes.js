'use strict';
// Renders one probe diagram under every bundled theme, and reports which ones
// produced a real diagram vs the PlantUML error image.
// usage: node all-themes.js <engine-file>
const path = require('path'), http = require('http'), fs = require('fs'), crypto = require('crypto');
const SP = 'C:/Code/Kronikol/tools/render-bench';
const pw = require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const engine = process.argv[2];
if (!engine) { console.error('usage: node all-themes.js <engine>'); process.exit(2); }

// theme names straight out of the generated map, so the list cannot drift
const g = {};
new Function('self', fs.readFileSync(path.join(SP, 'themes.js'), 'utf8'))(g);
const NAMES = Object.keys(g.PLANTUML_THEMES).sort();

const html = `<!doctype html><html><head></head><body><div id="out"></div>
<script src="/viz-global.js"></script>
<script type="module">import {render} from '/${path.basename(engine)}';window.__render=(l,i)=>render(l,i,{});window.__ready=1;</script>
</body></html>`;

const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  if (u === '/index.html') { res.setHeader('content-type', 'text/html'); return res.end(html); }
  const p = path.join(SP, u);
  if (!fs.existsSync(p)) { res.statusCode = 404; return res.end(); }
  res.setHeader('content-type', 'application/javascript');
  res.setHeader('cache-control', 'no-store');
  fs.createReadStream(p).pipe(res);
});

// the PlantUML error image is recognisable by its green-on-black palette
const isError = svg => svg.includes('#33FF02') && svg.includes('#FF0000');

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction('window.__ready && window.__render', null, { timeout: 120000 });

  const ok = [], failed = [], hashes = new Map();
  for (const name of NAMES) {
    const lines = ['@startuml', `!theme ${name}`, 'Alice -> Bob: hello',
      'Bob --> Alice: hi', 'note right: a note', '@enduml'];
    const r = await page.evaluate(async ({ lines }) => {
      const out = document.getElementById('out'); out.innerHTML = '';
      const done = new Promise(res => {
        const mo = new MutationObserver(() => {
          if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); }
        });
        mo.observe(out, { childList: true, subtree: true });
      });
      let err = null;
      try { window.__render(lines, 'out'); } catch (e) { err = String((e && e.message) || e); }
      if (!err) await Promise.race([done, new Promise(r => setTimeout(r, 60000))]);
      const svg = out.querySelector('svg');
      return { err, svg: svg ? svg.outerHTML : null };
    }, { lines });

    if (r.err || !r.svg || isError(r.svg)) { failed.push(name); continue; }
    ok.push(name);
    hashes.set(name, crypto.createHash('sha256').update(r.svg).digest('hex').slice(0, 12));
  }

  console.log(`engine: ${path.basename(engine)}`);
  console.log(`themes rendered OK : ${ok.length}/${NAMES.length}`);
  if (failed.length) console.log(`FAILED             : ${failed.join(', ')}`);
  // how many produced visually distinct output (a theme that silently does nothing
  // would collide with the others)
  const distinct = new Set(hashes.values()).size;
  console.log(`distinct SVG hashes: ${distinct}/${ok.length}`);
  const collisions = {};
  for (const [n, h] of hashes) (collisions[h] ||= []).push(n);
  for (const [h, ns] of Object.entries(collisions))
    if (ns.length > 1) console.log(`  same output: ${ns.join(', ')}`);

  await browser.close();
  server.close();
})();
