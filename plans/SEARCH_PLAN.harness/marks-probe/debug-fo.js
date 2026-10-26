// Range rects inside the foreignObject box, against a <mark> around the same characters and the box's own rect.
const path = require('path');
const { pathToFileURL } = require('url');
const engine = process.argv[2] || 'webkit';
const pw = require(engine === 'chromium' ? 'C:/Code/Kronikol4J/playwright/node_modules/playwright-core' : 'C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package');
(async () => {
  const b = await pw[engine].launch();
  const page = await (await b.newContext({ viewport: { width: 1400, height: 1000 } })).newPage();
  await page.goto(pathToFileURL(path.join(__dirname, 'markup.html')).href);
  await page.waitForTimeout(800);
  await page.addScriptTag({ path: path.join(__dirname, 'probe-lib.js') });
  for (const st of ['Z0', 'Zin', 'Zout', 'Zvb', 'Ztf', 'Zanc']) {
    const r = await page.evaluate(async (st) => {
      const t = P.resolve('o-box'); P.zoom(st, t); P.show(t); await P.raf2();
      const f = v => +v.toFixed(1);
      const rg = P.range(t), rr = rg.getBoundingClientRect();
      const fo = t.fo.getBoundingClientRect(), d = t.el.getBoundingClientRect();
      const svgR = t.svg.getBoundingClientRect();
      // the same characters in a <mark>
      const txt = t.node.data, mark = document.createElement('mark');
      const pre = document.createTextNode(txt.slice(0, t.s)), post = document.createTextNode(txt.slice(t.e));
      mark.textContent = txt.slice(t.s, t.e);
      t.el.replaceChild(post, t.node); t.el.insertBefore(mark, post); t.el.insertBefore(pre, mark);
      await P.raf2();
      const mr = mark.getBoundingClientRect();
      pre.remove(); mark.remove(); t.el.replaceChild(t.node, post);
      return { st, range: [rr.left, rr.top, rr.width, rr.height].map(f), mark: [mr.left, mr.top, mr.width, mr.height].map(f), div: [d.left, d.top, d.width, d.height].map(f), fo: [fo.left, fo.top, fo.width, fo.height].map(f), svg: [svgR.left, svgR.top, svgR.width, svgR.height].map(f), divClientH: t.el.clientHeight };
    }, st);
    console.log(engine, JSON.stringify(r));
    await page.screenshot({ path: path.join(__dirname, `_dbg-fo-${engine}-${st}.png`) });
  }
  await b.close();
})();
