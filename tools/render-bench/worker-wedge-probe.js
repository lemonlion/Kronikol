// Does one diagram whose note carries <&check> stall the shipped BrowserJs worker path, and does it take later
// diagrams with it? Pages are the real render script (DiagramContextMenu.GetPlantUmlBrowserRenderScript) with
// 1 and with 4 workers, engine from the real CDN pin, opened from file:// in the E2E project's Chromium.
//   dotnet run --project emitter-corpus -- --shim <shim-1.html> 1   (and again with 4)
//   node worker-wedge-probe.js <shim-1.html> <shim-4.html> [waitSeconds=100]
// Measured 2026-09-25 on the pinned fork build (plans/ENGINE_PIN_PLAN.md §1.15; results/payload-syntax-2026-09-25.txt):
// with one worker nothing after the icon diagram was drawn in 100 s; with four, each icon or emoji diagram held its worker.
const path = require('path'), url = require('url');
const pw = require(path.resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const [page1, page4, waitArg] = process.argv.slice(2);
const WAIT = +(waitArg || 100) * 1000;
const doc = (body, n) => `@startuml\n!pragma teoz true\nparticipant A\nparticipant B\nA -> B : call ${n}\nnote right\n${body}\nend note\nB --> A : ok\n@enduml`;
const order = ['plain', 'icon', 'plain', 'plain', 'plain', 'plain', 'emoji', 'plain', 'plain'];
const sources = order.map((k, i) => doc(k === 'icon' ? '{ "icon": "<&check>" }' : k === 'emoji' ? '{ "e": "<:smile:>" }' : `{ "n": ${i} }`, i));
(async () => {
  const browser = await pw.chromium.launch();
  for (const file of [page1, page4]) {
    const page = await browser.newPage();
    const logs = [];
    page.on('console', m => { if (/PlantUML|Kronikol|worker/i.test(m.text())) logs.push(m.type() + ': ' + m.text().slice(0, 140)); });
    await page.goto(url.pathToFileURL(path.resolve(file)).href);
    await page.waitForFunction(() => window.__kronikolRender && window.__kronikolRender.mode === 'worker' && window.__kronikolRender.workers > 0, null, { timeout: 120000, polling: 200 });
    const result = await page.evaluate(([sources, wait]) => new Promise(resolve => {
      const t0 = performance.now(), out = sources.map(() => null);
      sources.forEach((src, i) => {
        const el = document.createElement('div'); el.id = 'wedge-' + i; document.body.appendChild(el);
        new MutationObserver((m, mo) => {
          if (el.querySelector('svg')) { out[i] = 'svg +' + Math.round(performance.now() - t0) + ' ms'; mo.disconnect(); }
          else if (el.textContent && el.textContent.trim()) { out[i] = 'text +' + Math.round(performance.now() - t0) + ' ms: ' + el.textContent.trim().slice(0, 60); }
        }).observe(el, { childList: true, subtree: true, characterData: true });
        window.plantuml.render(src.split('\n'), el.id);
      });
      setTimeout(() => resolve({ out, telemetry: JSON.parse(JSON.stringify(window.__kronikolRender)) }), wait);
    }), [sources, WAIT]);
    console.log(JSON.stringify({ page: path.basename(file), order, rendered: result.out.map((r, i) => `${i}-${order[i]}: ${r || 'NOT RENDERED'}`),
      telemetry: { mode: result.telemetry.mode, workers: result.telemetry.workers, renders: result.telemetry.renders, errors: result.telemetry.errors, inFlight: result.telemetry.inFlight }, console: logs.slice(0, 6) }, null, 1));
    await page.close();
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
