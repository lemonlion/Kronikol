// What describeEngineFailure's stack check costs a report (4.14.6 added `window._isStackOverflow(el.innerHTML)` to it,
// and it runs after every diagram the page draws). Opens a report, draws every diagram, then for each drawn container
// times: the innerHTML serialization, the detector on that string, and a DOM-only pre-check (first <text> starts
// "PlantUML ") that would skip the serialization for a drawn diagram.
//   node stackcheck-cost.js <TestRunReport.html> [runs]
const path = require('path'), url = require('url');
const pw = require(process.env.BENCH_PW || path.resolve(__dirname, '../../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
(async () => {
  const file = process.argv[2], runs = +(process.argv[3] || 3);
  const browser = await pw.chromium.launch({ args: process.env.JSFLAGS ? ['--js-flags=' + process.env.JSFLAGS] : [] });
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
  await page.goto(url.pathToFileURL(path.resolve(file)).href);
  await page.waitForFunction(() => typeof window._renderDiagramsInContainer === 'function', null, { timeout: 120000, polling: 200 });
  const t0 = Date.now();
  await page.evaluate(() => window._renderDiagramsInContainer(document.body));
  await page.waitForFunction(() => {
    const all = Array.from(document.querySelectorAll('.plantuml-browser'));
    return all.length > 0 && all.every(el => el.dataset.rendered === '1');
  }, null, { timeout: 900000, polling: 500 });
  const drawMs = Date.now() - t0;
  const results = [];
  for (let r = 0; r < runs; r++) {
    results.push(await page.evaluate(() => {
      const els = Array.from(document.querySelectorAll('.plantuml-browser')).filter(el => el.querySelector('svg'));
      let ser = 0, det = 0, pre = 0, bytes = 0, maxSer = 0, flagged = 0;
      for (const el of els) {
        let a = performance.now();
        const html = el.innerHTML;
        let b = performance.now();
        const hit = window._isStackOverflow(html);
        let c = performance.now();
        const t = el.querySelector('svg text');
        const may = !!t && (t.textContent || '').indexOf('PlantUML ') === 0;
        let d = performance.now();
        ser += b - a; det += c - b; pre += d - c; bytes += html.length; maxSer = Math.max(maxSer, b - a); if (hit) flagged++;
      }
      const k = window.__kronikolRender || {};
      return { diagrams: els.length, mb: +(bytes / 1048576).toFixed(1), serializeMs: +ser.toFixed(1), detectorMs: +det.toFixed(1),
        maxSerializeMs: +maxSer.toFixed(2), domPrecheckMs: +pre.toFixed(1), flagged, workerMs: k.workerMs, injectMs: +(k.injectMs || 0).toFixed(1), renders: k.renders, mode: k.mode };
    }));
  }
  console.log(JSON.stringify({ file: path.basename(path.dirname(file)) + '/' + path.basename(file), drawMs, results }, null, 0));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
