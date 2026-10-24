'use strict';
// How the browser engine's teoz drawing time grows with a sequence diagram's length, on made-up diagrams: 5
// participants, N request and reply pairs with short labels, with and without a 6-line note on each reply, teoz and
// (for comparison) the classic layout. Each drawing misses the cache. Uses a report page only for its engine.
// usage: node probe-teoz-scaling.js <report.html> [sizes, default 50,100,200,400,800]
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
  const [file, sizesArg = '50,100,200,400,800'] = process.argv.slice(2);
  const sizes = sizesArg.split(',').map(Number);
  const browser = await launchInstalled(chromium, { headless: true });
  try {
    const page = await (await browser.newContext({ viewport: { width: 1920, height: 1000 } })).newPage();
    await page.goto('file:///' + path.resolve(file).split(path.sep).join('/'), { timeout: 600000 });
    // the engine loads with the first diagram drawn: draw one scenario
    await page.evaluate(() => { const sc = document.querySelector('details.scenario'); let p = sc; while (p) { if (p.tagName === 'DETAILS') p.open = true; p = p.parentElement; } window._renderDiagramsInContainer(sc); });
    await page.waitForFunction(() => window.plantuml && typeof window.plantuml.render === 'function' && document.querySelector('details.scenario svg'), null, { timeout: 300000, polling: 500 });
    await page.waitForTimeout(2000);
    const out = await page.evaluate(async (sizes) => {
      let seq = 0;
      function render(source) {
        return new Promise((resolve) => {
          const div = document.createElement('div');
          div.id = 'teoz-scaling-' + (seq++);
          div.style.cssText = 'position:absolute;left:-300000px;top:0;visibility:hidden';
          document.body.appendChild(div);
          const t0 = performance.now();
          const mo = new MutationObserver(() => done());
          function done() {
            const svg = div.querySelector('svg');
            if (!svg && !div.textContent.trim()) return;
            mo.disconnect();
            const r = svg ? { ms: performance.now() - t0, h: svg.height.baseVal.value } : { ms: performance.now() - t0, error: div.textContent.trim().slice(0, 120) };
            div.remove();
            resolve(r);
          }
          mo.observe(div, { childList: true, subtree: true, characterData: true });
          window.plantuml.render(source.split('\n'), div.id, { maxSvgSize: 0 });
          done();
        });
      }
      function source(n, teoz, notes) {
        const ps = ['caller', 'api', 'cache', 'db', 'queue'];
        const l = ['@startuml', "' scaling " + Math.random().toString(36).slice(2)];
        if (teoz) l.push('!pragma teoz true');
        l.push('autonumber 1', 'actor Caller as caller', 'participant "Api" as api', 'participant "Cache" as cache', 'database "Db" as db', 'queue "Queue" as queue');
        for (let i = 0; i < n; i++) {
          const to = ps[1 + (i % 4)], from = i % 3 === 0 ? 'caller' : 'api';
          if (from === to) continue;
          l.push(`${from} -> ${to}: GET /items/${i}?page=${i % 7}`);
          l.push(`${to} --> ${from}: 200 OK`);
          if (notes) l.push('note right', '{', `  "id": ${i},`, '  "name": "item",', '  "tags": ["a", "b"],', '  "ok": true', '}', 'end note');
        }
        l.push('@enduml');
        return l.join('\n');
      }
      const rows = [];
      for (const n of sizes) for (const [teoz, notes] of [[true, false], [true, true], [false, true]]) {
        const r = await render(source(n, teoz, notes));
        rows.push({ n, teoz, notes, ...r });
      }
      return rows;
    }, sizes);
    console.log('pairs | layout | notes | ms | ms per pair | height');
    for (const r of out) console.log(`${r.n} | ${r.teoz ? 'teoz' : 'classic'} | ${r.notes ? 'yes' : 'no'} | ${r.error ? 'ERROR ' + r.error : Math.round(r.ms)} | ${r.error ? '-' : (r.ms / r.n).toFixed(2)} | ${r.h ? Math.round(r.h) : '-'}`);
  } finally {
    await browser.close();
  }
})();
