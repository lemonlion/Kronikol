'use strict';
// Verifies !theme support in a TeaVM PlantUML engine build.
// usage: node verify-themes.js <engine-file> [--no-themes-js] [--preregister]
//
// Serves the engine + themes.js from the render-bench dir over http, renders a
// set of theme probes in headless Chromium and reports what came back.
const path = require('path'), http = require('http'), fs = require('fs');
const SP = 'C:/Code/Kronikol/tools/render-bench';
const PW = 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);

const engine = process.argv[2];
const noThemesJs = process.argv.includes('--no-themes-js');
const preregister = process.argv.includes('--preregister');
if (!engine) { console.error('usage: node verify-themes.js <engine> [--no-themes-js] [--preregister]'); process.exit(2); }

const THEMES_JS = path.join(SP, 'themes.js');

// The probes. Each is [label, puml lines].
const PROBES = [
  ['theme amiga', ['@startuml', '!theme amiga', 'Alice -> Bob: hello', 'Bob --> Alice: hi', '@enduml']],
  ['theme hacker', ['@startuml', '!theme hacker', 'Alice -> Bob: hello', 'Bob --> Alice: hi', '@enduml']],
  ['theme cerulean', ['@startuml', '!theme cerulean', 'Alice -> Bob: hello', 'Bob --> Alice: hi', '@enduml']],
  ['theme bogus name', ['@startuml', '!theme zzz-does-not-exist', 'Alice -> Bob: hello', '@enduml']],
  ['get_current_theme after !theme amiga',
    ['@startuml', '!theme amiga', '!$m = %get_current_theme()', 'Alice -> Bob: $m.display_name', '@enduml']],
  ['get_all_theme count', ['@startuml', '!$a = %get_all_theme()', 'Alice -> Bob: count=%size($a)', '@enduml']],
  ['no theme (control)', ['@startuml', 'Alice -> Bob: hello', 'Bob --> Alice: hi', '@enduml']],
  ['theme name from a variable',
    ['@startuml', '!$t = "hacker"', '!theme $t', 'Alice -> Bob: hello', 'Bob --> Alice: hi', '@enduml']],
  ['theme from <stdlib> (unsupported)',
    ['@startuml', '!theme amiga from <archimate>', 'Alice -> Bob: hello', '@enduml']],
  ['theme after other preproc',
    ['@startuml', '!$x = 1', '!if $x == 1', '!theme cerulean', '!endif',
      'Alice -> Bob: hello', 'Bob --> Alice: hi', '@enduml']],
];

const preregSnippet = preregister
  ? `<script src="/themes-data.js"></script>`
  : '';

const html = `<!doctype html><html><head></head><body><div id="out"></div>
${preregSnippet}
<script src="/viz-global.js"></script>
<script type="module">import {render} from '/${path.basename(engine)}';window.__render=(lines,id)=>render(lines,id,{});window.__ready=1;</script>
</body></html>`;

const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  if (u === '/index.html') { res.setHeader('content-type', 'text/html'); return res.end(html); }
  // --no-themes-js: pretend the companion script was never deployed
  if (u === '/themes.js' && noThemesJs) { res.statusCode = 404; return res.end(); }
  // pre-registration source: the same generated file, served under another name
  const p = u === '/themes-data.js' ? THEMES_JS : path.join(SP, u);
  if (!fs.existsSync(p)) { res.statusCode = 404; return res.end(); }
  res.setHeader('content-type', 'application/javascript');
  res.setHeader('cache-control', 'no-store');
  fs.createReadStream(p).pipe(res);
});

const fills = svg => {
  const m = svg.match(/fill="#[0-9A-Fa-f]{6}"/g) || [];
  const counts = {};
  for (const f of m) counts[f] = (counts[f] || 0) + 1;
  return Object.entries(counts).sort((a, b) => b[1] - a[1]).slice(0, 3)
    .map(([f, n]) => `${f.slice(7, -1)}x${n}`).join(' ');
};
const texts = svg => (svg.match(/>[^<>]+</g) || []).map(t => t.slice(1, -1).trim())
  .filter(t => t && t !== 'Alice' && t !== 'Bob').join(' | ');

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  const pageErrors = [];
  page.on('pageerror', e => pageErrors.push(e.message));
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction('window.__ready && window.__render', null, { timeout: 120000 });

  console.log(`engine: ${path.basename(engine)}`);
  console.log(`themes.js served: ${!noThemesJs}   pre-registered: ${preregister}`);
  console.log('-'.repeat(100));

  for (const [label, lines] of PROBES) {
    const r = await page.evaluate(async ({ lines }) => {
      const out = document.getElementById('out'); out.innerHTML = '';
      let err = null;
      const done = new Promise(res => {
        const mo = new MutationObserver(() => {
          if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); }
        });
        mo.observe(out, { childList: true, subtree: true });
      });
      try { window.__render(lines, 'out'); } catch (e) { err = String((e && e.message) || e); }
      if (!err) await Promise.race([done, new Promise(r => setTimeout(r, 60000))]);
      const svg = out.querySelector('svg');
      return { err: err || (svg ? null : out.textContent.slice(0, 300)), svg: svg ? svg.outerHTML : null };
    }, { lines });

    if (r.err || !r.svg) { console.log(`${label.padEnd(38)} ERROR/NO-SVG: ${String(r.err).slice(0, 60)}`); continue; }
    console.log(`${label.padEnd(38)} ${fills(r.svg).padEnd(34)} ${texts(r.svg).slice(0, 60)}`);
    fs.writeFileSync(path.join(__dirname, 'out-' + label.replace(/[^a-z0-9]+/gi, '-') + '.svg'), r.svg);
  }

  if (pageErrors.length) console.log('\npage errors:', pageErrors.slice(0, 3));
  await browser.close();
  server.close();
})();
