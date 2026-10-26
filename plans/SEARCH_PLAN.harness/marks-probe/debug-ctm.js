// getScreenCTM vs getBoundingClientRect for an SVG <text>, under scroll containers and transforms.
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
  const cases = [
    ['Z0 window scrolled', 'o-zwsp', 'Z0', null],
    ['Ztf (container scrolls both ways)', 'o-zwsp', 'Ztf', null],
    ['Ztf, container scrollTop forced 0', 'o-zwsp', 'Ztf', 'top0'],
    ['Zanc (ancestor scale .75)', 'o-zwsp', 'Zanc', null],
    ['Zanc, window at top', 's0', 'Zanc', null],
    ['Zin (container scrolls x)', 'm-json', 'Zin', null],
    ['Zin, container scrolled y? (no)', 'o-zwsp', 'Zin', null],
    ['overflow-y container scrolled 300', 'o-zwsp', 'Z0', 'ycont'],
  ];
  for (const [label, id, st, extra] of cases) {
    const r = await page.evaluate(async ([id, st, extra]) => {
      const t = P.resolve(id); P.zoom(st, t);
      const c = t.svg.parentElement;
      if (extra === 'ycont') { c.style.height = '400px'; c.style.overflowY = 'auto'; }
      P.show(t);
      if (extra === 'top0') { /* nothing: report what show() left */ }
      await P.raf2();
      const bb = t.el.getBBox();
      const viaCtm = P.mapRect(t.el, { x: bb.x, y: bb.y, w: bb.width, h: bb.height });
      const g = t.el.getBoundingClientRect();
      const out = { win: [scrollX, scrollY], cont: [c.scrollLeft, c.scrollTop], gbcr: [g.left, g.top, g.width, g.height].map(v => +v.toFixed(1)), ctm: [viaCtm.x, viaCtm.y, viaCtm.w, viaCtm.h].map(v => +v.toFixed(1)) };
      out.d = [+(viaCtm.x - g.left).toFixed(1), +(viaCtm.y - g.top).toFixed(1)];
      // a <rect> inserted at the bbox in user space: where does it land?
      const NS = 'http://www.w3.org/2000/svg', rr = document.createElementNS(NS, 'rect');
      rr.setAttribute('x', bb.x); rr.setAttribute('y', bb.y); rr.setAttribute('width', bb.width); rr.setAttribute('height', bb.height); rr.setAttribute('fill', 'none');
      t.el.parentNode.insertBefore(rr, t.el);
      const q = rr.getBoundingClientRect(); out.rectGbcr = [q.left, q.top].map(v => +v.toFixed(1)); rr.remove();
      if (extra === 'ycont') { c.style.height = ''; c.style.overflowY = ''; }
      return out;
    }, [id, st, extra]);
    console.log(engine, label.padEnd(36), JSON.stringify(r));
  }
  await b.close();
})();
