// Load timing for prototype compressed report pages in Playwright's bundled engines (headless; never installed Chrome/Edge).
// node bench.js <variants dir> [variants csv] [browsers csv] [runs] [query]
const { chromium, firefox, webkit } = require('playwright-core');
const { pathToFileURL } = require('url');
const path = require('path');

const dir = process.argv[2];
const variants = (process.argv[3] || 'v0-probed,vA-asis,vB-one,vC-two').split(',');
const browsers = (process.argv[4] || 'chromium,firefox,firefox+zstd,webkit').split(',');
const runs = +(process.argv[5] || 3);
const query = process.argv[6] || '';

async function launch(name) {
  if (name === 'chromium') return chromium.launch({ headless: true });
  if (name === 'webkit') return webkit.launch({ headless: true });
  const prefs = name === 'firefox+zstd' ? { 'dom.compression_streams.zstd.enabled': true } : {};
  return firefox.launch({ headless: true, firefoxUserPrefs: prefs });
}

async function one(browser, bname, variant) {
  const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(String(e.message).slice(0, 160)));
  const url = pathToFileURL(path.join(dir, variant + '.html')).href + query;
  const t0 = Date.now();
  await page.goto(url, { waitUntil: 'commit', timeout: 180000 });
  await page.waitForFunction(() => {
    const T = window.__krnT;
    if (!T) return false;
    if (T.diagStart && !T.diagDone) return false;
    return !!T.paintDcl && (!!T.paint2 || performance.now() - T.paintDcl > 5000);
  }, null, { polling: 200, timeout: 90000 });
  const wall = Date.now() - t0;
  const r = await page.evaluate(async () => {
    const T = window.__krnT;
    const nav = performance.getEntriesByType('navigation')[0] || {};
    const fcp = (performance.getEntriesByName('first-contentful-paint')[0] || {}).startTime;
    const el = document.querySelector('.plantuml-browser[id]');
    let srcLen = -1, srcOk = false;
    try {
      const z = window._getPumlZ ? window._getPumlZ(el) : null;
      const src = z ? await window.decompressGzipBase64(z) : null;
      if (src) { srcLen = src.length; srcOk = src.slice(0, 9) === '@startuml'; }
    } catch (e) { srcLen = -2; }
    return { T, fcp, navDcl: nav.domContentLoadedEventEnd, navLoad: nav.loadEventEnd,
      details: document.querySelectorAll('details').length, srcLen, srcOk, charset: document.characterSet,
      nativeZstd: (() => { try { new DecompressionStream('zstd'); return true; } catch (e) { return false; } })() };
  });
  let heapMB = null;
  if (bname === 'chromium') {
    const cdp = await context.newCDPSession(page);
    await cdp.send('HeapProfiler.collectGarbage');
    await cdp.send('Performance.enable');
    const m = await cdp.send('Performance.getMetrics');
    const u = await cdp.send('Runtime.getHeapUsage');
    heapMB = Math.round((m.metrics.find(x => x.name === 'JSHeapUsedSize') || {}).value / 1048576) + '+' +
      Math.round((u.backingStorageSize || 0) / 1048576);
  }
  await context.close();
  return { wall, heapMB, errors, ...r };
}

const f = v => (v == null || isNaN(v)) ? '-' : Math.round(v).toString();
(async () => {
  console.log(['browser', 'variant', 'run', 'decoder', 'shell', 'start', 'inflate', 'text', 'write', 'dcl2', 'load2', 'paint2', 'diagDone',
    'paintDcl', 'fcp', 'wall', 'heapMB', 'details', 'src', 'errors'].join('\t'));
  for (const bname of browsers) {
    const browser = await launch(bname);
    for (const variant of variants) {
      for (let i = 1; i <= runs; i++) {
        let r;
        try { r = await one(browser, bname, variant); }
        catch (e) { console.log([bname, variant, i, 'FAILED', String(e.message).split('\n')[0].slice(0, 120)].join('\t')); continue; }
        const T = r.T;
        console.log([bname + (i === 1 ? ' ' + browser.version() : ''), variant, i, T.decoder || '-', f(T.shell), f(T.start),
          f(T.inflated - T.start), f(T.decoded - T.inflated), f(T.written - T.decoded), f(T.dcl2), f(T.load2), f(T.paint2),
          f(T.diagDone), f(T.paintDcl), f(r.fcp), r.wall, r.heapMB ?? '-', r.details, (r.srcOk ? 'ok ' : 'BAD ') + r.srcLen,
          r.errors.length ? r.errors.length + ': ' + r.errors[0] : '0'].join('\t'));
      }
    }
    await browser.close();
  }
})();
