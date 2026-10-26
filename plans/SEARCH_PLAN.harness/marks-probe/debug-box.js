const path = require('path');
const { pathToFileURL } = require('url');
const engine = process.argv[2] || 'chromium';
const pw = require(engine === 'chromium' ? 'C:/Code/Kronikol4J/playwright/node_modules/playwright-core' : 'C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package');
(async () => {
  const b = await pw[engine].launch();
  const page = await (await b.newContext({ viewport: { width: 1400, height: 1000 } })).newPage();
  await page.goto(pathToFileURL(path.join(__dirname, 'markup.html')).href);
  await page.waitForTimeout(800);
  await page.addScriptTag({ path: path.join(__dirname, 'probe-lib.js') });
  for (const st of ['Z0', 'Ztf', 'Zanc']) {
    const r = await page.evaluate(async (st) => {
      const t = P.resolve('o-box'); P.zoom(st, t); P.show(t); await P.raf2();
      const m = P.measure(t);
      const fo = t.fo.getBoundingClientRect(), d = t.el.getBoundingClientRect(), c = t.svg.parentElement;
      return { st, R: m.R, fo: [fo.left, fo.top, fo.width, fo.height], div: [d.left, d.top, d.width, d.height], cont: [c.scrollLeft, c.scrollTop, c.clientWidth, c.clientHeight, getComputedStyle(c).overflowY], win: [scrollX, scrollY] };
    }, st);
    console.log(JSON.stringify(r));
    await page.screenshot({ path: path.join(__dirname, `_dbg-box-${engine}-${st}.png`) });
  }
  // phase-e like: overlays then ink
  const r2 = await page.evaluate(async () => {
    const t = P.resolve('o-box'); P.zoom('Z0', t); P.show(t); await P.raf2();
    const placed = P.placeOverlays(t); await P.raf2();
    const m = P.measure(P.resolve('o-box'));
    return { placed, R: m.R.u, ov: P.overlayRects() };
  });
  console.log(JSON.stringify(r2));
  await b.close();
})();
