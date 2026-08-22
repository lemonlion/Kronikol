'use strict';
// Objective benchmark of a real Kronikol report in Chromium.
// usage: node bench-report.js <variantName|baseline> [reportPath]
// Serves the report folder over http; rewrites the CDN engine URLs to local files; applies variant replacements.
const path = require('path'), http = require('http'), fs = require('fs'), url = require('url');
const SP = __dirname;
const PW = 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const variantName = process.argv[2] || 'baseline';
const reportPath = process.argv[3] || 'C:/Code/work/sidekick-intelligence-e2e/.logs/kronikol/TestRunReport.html';
const variant = variantName === 'baseline' ? { engine: 'old-plantuml.js', viz: 'viz-global.js', replace: [] } : require(path.join(SP, 'variants', variantName + '.js'));
const reportDir = path.dirname(reportPath);
const CDN = 'https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.3beta6-patched';

let html = fs.readFileSync(reportPath, 'utf8').split('\r\n').join('\n');
html = html.split(CDN + '/viz-global.js').join('/__engine/viz-global.js').split(CDN + '/plantuml.js').join('/__engine/plantuml.js');
for (const [from, to] of (variant.replace || [])) {
  if (!html.includes(from)) { console.error('VARIANT REPLACEMENT NOT FOUND: ' + from.slice(0, 80)); process.exit(2); }
  html = html.split(from).join(to);
}
if (variant.transform) html = variant.transform(html);

const mime = { '.html': 'text/html', '.js': 'application/javascript', '.json': 'application/json', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml' };
const server = http.createServer((req, res) => {
  const u = decodeURIComponent(req.url.split('?')[0]);
  let file = null;
  if (u === '/TestRunReport.html' || u === '/') { res.setHeader('content-type', 'text/html; charset=utf-8'); return res.end(html); }
  if (u.startsWith('/__engine/')) {
    const name = u.slice('/__engine/'.length);
    file = path.join(SP, name === 'plantuml.js' ? variant.engine : name === 'viz-global.js' ? (variant.viz || 'viz-global.js') : name);
  } else if (u.startsWith('/__sp/')) {
    file = path.join(SP, u.slice('/__sp/'.length));
  } else file = path.join(reportDir, u);
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.statusCode = 404; return res.end('404 ' + u); }
  res.setHeader('content-type', mime[path.extname(file)] || 'application/octet-stream');
  res.setHeader('cache-control', 'no-store');
  res.setHeader('access-control-allow-origin', '*'); // jsDelivr sends this too
  fs.createReadStream(file).pipe(res);
});

const initScript = `
(function(){
  window.__bench = { longTasks: [], renderedAt: {}, t0: performance.now() }; setInterval(() => { try { console.warn('[mem] heap ' + Math.round(performance.memory.usedJSHeapSize/1048576) + ' MB, rendered ' + document.querySelectorAll('.plantuml-browser[data-rendered], .puml-fragment[data-rendered]').length); } catch(e) {} }, 3000);
  try { new PerformanceObserver(list => { for (const e of list.getEntries()) window.__bench.longTasks.push({ start: Math.round(e.startTime), dur: Math.round(e.duration) }); }).observe({ type: 'longtask', buffered: true }); } catch (e) {}
  document.addEventListener('DOMContentLoaded', () => {
    const mo = new MutationObserver(muts => {
      for (const m of muts) {
        if (m.type === 'attributes' && m.attributeName === 'data-rendered' && m.target.dataset.rendered === '1') {
          const id = m.target.id; if (!window.__bench.renderedAt[id]) window.__bench.renderedAt[id] = Math.round(performance.now());
        }
      }
    });
    mo.observe(document.body, { attributes: true, subtree: true, attributeFilter: ['data-rendered'] });
  });
})();`;

function summarizeLongTasks(tasks, from, to) {
  const sel = tasks.filter(t => t.start >= from && t.start <= to);
  const tbt = sel.reduce((a, t) => a + Math.max(0, t.dur - 50), 0);
  const max = sel.reduce((a, t) => Math.max(a, t.dur), 0);
  const over1s = sel.filter(t => t.dur >= 1000).length;
  return { count: sel.length, tbtMs: tbt, maxMs: max, tasksOver1s: over1s };
}

(async () => {
  await new Promise(r => server.listen(18765, '127.0.0.1', r));
  const port = server.address().port;
  let startUrl = 'http://127.0.0.1:' + port + '/TestRunReport.html';
  if (process.env.FILEMODE === '1') { const fp = path.join(SP, 'results', 'report-filemode.html'); fs.writeFileSync(fp, html); startUrl = 'file:///' + fp.split('\\').join('/'); console.error('[bench] file mode', startUrl); }
  const browser = await pw.chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await context.newPage();
  page.on('crash', () => console.error('[bench] PAGE CRASHED'));
  page.on('pageerror', e => console.error('PAGEERROR', String(e.message).slice(0, 200)));
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') console.error('CONSOLE', m.text().slice(0, 200)); });
  await page.addInitScript(initScript);
  if (variant.initScript) await page.addInitScript(variant.initScript);
  const R = { variant: variantName, engine: variant.engine };

  const tNav = performance.now();
  await page.goto(startUrl, { waitUntil: 'load' });
  R.loadMs = Math.round(performance.now() - tNav);
  await page.waitForFunction(() => document.body.classList.contains('plantuml-ready'), null, { timeout: 120000, polling: 200 });
  R.readyMs = Math.round(performance.now() - tNav); console.error('[bench] ready', R.readyMs);

  // Phase 1: initial (first scenario / viewport) diagrams — wait until queue drains (no unrendered queued diagrams)
  const phase1Start = await page.evaluate(() => performance.now()); console.error('[bench] phase1 wait');
  await page.waitForFunction(() => {
    const queued = Array.from(document.querySelectorAll('.plantuml-browser[data-queued]'));
    if (queued.length === 0) return false;
    return queued.every(el => el.dataset.rendered === '1' && Array.from(el.querySelectorAll('.puml-fragment')).every(f => f.dataset.rendered === '1'));
  }, null, { timeout: 600000, polling: 200 });
  const p1 = await page.evaluate(() => ({ now: performance.now(), queued: document.querySelectorAll('.plantuml-browser[data-queued]').length, ids: Array.from(document.querySelectorAll('.plantuml-browser[data-queued]')).map(e => e.id) }));
  R.phase1 = { diagrams: p1.queued, ids: p1.ids, ms: Math.round(p1.now - phase1Start) }; console.error('[bench] phase1 done', JSON.stringify(R.phase1));
  const firstRendered = await page.evaluate(() => { const v = Object.values(window.__bench.renderedAt); return v.length ? Math.min(...v) : null; });
  R.firstDiagramMs = firstRendered === null ? null : Math.round(firstRendered - phase1Start);

  // Phase 2: force-render everything
  const phase2Start = await page.evaluate(() => { const t = performance.now(); window._renderDiagramsInContainer(document.body); return t; });
  await page.waitForFunction(() => Array.from(document.querySelectorAll('.plantuml-browser')).every(el => el.dataset.rendered === '1' && Array.from(el.querySelectorAll('.puml-fragment')).every(f => f.dataset.rendered === '1')), null, { timeout: 900000, polling: 250 });
  const p2 = await page.evaluate(() => ({ now: performance.now(), total: document.querySelectorAll('.plantuml-browser').length, frags: document.querySelectorAll('.puml-fragment').length, svgs: document.querySelectorAll('.plantuml-browser svg').length, errors: Array.from(document.querySelectorAll('.plantuml-browser')).filter(e => /Render error|too large|Decompression error/.test(e.textContent || '')).map(e => e.id + ': ' + (e.textContent || '').trim().slice(0, 120)) }));
  R.phase2 = { diagrams: p2.total, fragments: p2.frags, svgs: p2.svgs, errors: p2.errors, ms: Math.round(p2.now - phase2Start) }; console.error('[bench] phase2 done', R.phase2.ms);

  // Long-task summary for both phases
  const lt = await page.evaluate(() => window.__bench.longTasks);
  R.longTasksPhase1 = summarizeLongTasks(lt, phase1Start, p1.now);
  R.longTasksPhase2 = summarizeLongTasks(lt, phase2Start, p2.now);

  // Phase 3: interaction — toggle a note on the biggest diagram and time the re-render
  const interaction = await page.evaluate(async () => {
    const containers = Array.from(document.querySelectorAll('.plantuml-browser')).filter(c => c.querySelector('svg .note-toggle-icon'));
    if (!containers.length) return { error: 'no note toggles found' };
    containers.sort((a, b) => b.querySelectorAll('svg *').length - a.querySelectorAll('svg *').length);
    const c = containers[0];
    const icon = c.querySelector('svg .note-toggle-icon');
    const target = icon.querySelector('rect') || icon;
    const svgBefore = c.querySelector('svg');
    const t0 = performance.now();
    const lt0 = window.__bench.longTasks.length; const ws0 = window.__workerStats ? JSON.parse(JSON.stringify(window.__workerStats)) : null;
    target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
    // done when: not rendering any more AND svg replaced (or fragments re-rendered)
    const deadline = t0 + 300000;
    while (performance.now() < deadline) {
      await new Promise(r => setTimeout(r, 50));
      const still = c._noteRendering || window._plantumlRendering;
      const svgNow = c.querySelector('svg');
      if (!still && svgNow && svgNow !== svgBefore) break;
      if (!still && performance.now() - t0 > 2000 && svgNow === svgBefore) { /* maybe cached swap w/o svg change */ }
    }
    const ms = performance.now() - t0;
    const tasks = window.__bench.longTasks.slice(lt0);
    const ws1 = window.__workerStats; return { container: c.id, svgElements: c.querySelectorAll('svg *').length, ms: Math.round(ms), workerRenders: ws1 && ws0 ? ws1.renders - ws0.renders : null, workerMs: ws1 && ws0 ? Math.round(ws1.workerMs - ws0.workerMs) : null, longTasks: tasks.length, maxTaskMs: tasks.reduce((a, t) => Math.max(a, t.dur), 0), tbtMs: tasks.reduce((a, t) => a + Math.max(0, t.dur - 50), 0) };
  });
  R.interaction = interaction;
  R.workerStats = await page.evaluate(() => window.__workerStats ? { renders: window.__workerStats.renders, workerMs: Math.round(window.__workerStats.workerMs), injectMs: Math.round(window.__workerStats.injectMs) } : null);
  R.jsHeapMB = await page.evaluate(() => performance.memory ? Math.round(performance.memory.usedJSHeapSize / 1048576) : null);
  await browser.close(); server.close();
  console.log(JSON.stringify(R, null, 1));
  fs.writeFileSync(path.join(SP, 'results', `report-${process.env.TAG || variantName}.json`), JSON.stringify(R, null, 1));
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
