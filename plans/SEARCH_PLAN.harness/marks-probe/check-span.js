// 1. textLength without tspans: does getExtentOfChar(0..n-1) span the painted text (whole text red, inline style)?
// 2. one multiply layer: compute vs insert+layout cost, 312 rects.
const path = require('path');
const { pathToFileURL } = require('url');
const engine = process.argv[2] || 'webkit';
const PW_CHROMIUM = 'C:/Code/Kronikol4J/playwright/node_modules/playwright-core';
const pw = require(engine === 'chromium' ? PW_CHROMIUM : 'C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package');
const { PNG } = require(PW_CHROMIUM + '/lib/utilsBundle');
(async () => {
  const b = await pw[engine].launch();
  const page = await (await b.newContext({ viewport: { width: 1400, height: 1000 } })).newPage();
  await page.goto(pathToFileURL(path.join(__dirname, 'markup.html')).href);
  await page.waitForTimeout(800);
  await page.addScriptTag({ path: path.join(__dirname, 'probe-lib.js') });
  const out = [];
  for (const id of ['s0', 's1', 's2', 's3', 's4', 's5', 's6']) {
    const m = await page.evaluate((id) => {
      const el = document.getElementById(id); el.scrollIntoView({ block: 'center' });
      const n = el.getNumberOfChars(), a = el.getExtentOfChar(0), z = el.getExtentOfChar(n - 1);
      const F = el.textContent.indexOf('Fresh'), f = el.getExtentOfChar(F), h = el.getExtentOfChar(F + 4);
      const u = P.mapRect(el, { x: a.x, y: a.y, w: z.x + z.width - a.x, h: a.height });
      const fr = P.mapRect(el, { x: f.x, y: f.y, w: h.x + h.width - f.x, h: f.height });
      const st = document.createElement('style'); st.id = 'iso'; st.textContent = 'svg text, .label, p { visibility: hidden !important } #' + id + ' { visibility: visible !important }'; document.head.appendChild(st);
      el.style.fill = '#ff0000';
      const g = el.getBoundingClientRect();
      return { span: u, fresh: fr, box: { x: g.left, y: g.top, w: g.width, h: g.height } };
    }, id);
    await page.evaluate(() => P.raf2());
    const clip = { x: Math.floor(m.box.x - 30), y: Math.floor(m.box.y - 6), width: Math.ceil(m.box.w + 60), height: Math.ceil(m.box.h + 12) };
    const png = PNG.sync.read(await page.screenshot({ clip }));
    let x1 = 1e9, x2 = -1e9;
    const cols = new Array(png.width).fill(0);
    for (let y = 0; y < png.height; y++) for (let x = 0; x < png.width; x++) { const i = (y * png.width + x) * 4; const r = png.data[i], g = png.data[i + 1], bl = png.data[i + 2]; if (r >= 120 && r - g >= 60 && r - bl >= 60) { x1 = Math.min(x1, x); x2 = Math.max(x2, x); cols[x]++; } }
    await page.evaluate((id) => { document.getElementById('iso').remove(); document.getElementById(id).style.fill = ''; }, id);
    const inkL = clip.x + x1, inkR = clip.x + x2 + 1;
    // ink columns inside the rect getExtentOfChar gives for "Fresh" (should hold ink, and the columns just outside it should be the gaps either side)
    const inFresh = cols.slice(Math.round(m.fresh.x - clip.x), Math.round(m.fresh.x + m.fresh.w - clip.x)).reduce((a, v) => a + v, 0);
    const total = cols.reduce((a, v) => a + v, 0);
    out.push({ id, spanVsInk: { l: +(m.span.x - inkL).toFixed(1), r: +(m.span.x + m.span.w - inkR).toFixed(1) }, freshShareOfInk: +(inFresh / total).toFixed(3) });
  }
  for (const o of out) console.log(engine, JSON.stringify(o));
  const t = await page.evaluate(async () => {
    const svg = document.querySelector('#d1 svg'); svg.scrollIntoView({ block: 'start' });
    const ms = []; svg.querySelectorAll('text').forEach(t => { let i = -1; while ((i = t.textContent.indexOf('"', i + 1)) >= 0) ms.push({ t, s: i }); });
    await P.raf2();
    const t0 = performance.now();
    const rs = ms.map(m => { const a = m.t.getExtentOfChar(m.s); return [a.x, a.y, a.width, a.height]; });
    const t1 = performance.now();
    const NS = 'http://www.w3.org/2000/svg', g = document.createElementNS(NS, 'g'); g.setAttribute('style', 'mix-blend-mode: multiply');
    rs.forEach(q => { const r = document.createElementNS(NS, 'rect'); r.setAttribute('x', q[0]); r.setAttribute('y', q[1]); r.setAttribute('width', q[2]); r.setAttribute('height', q[3]); r.setAttribute('fill', '#ffd400'); g.appendChild(r); });
    svg.appendChild(g); svg.getBoundingClientRect(); g.getBBox();
    const t2 = performance.now();
    await P.raf2();
    const t3 = performance.now();
    g.remove();
    await P.raf2();
    // the same rects, each inserted just before its own <text> (behind it)
    const t4 = performance.now();
    const made = rs.map((q, i) => { const r = document.createElementNS(NS, 'rect'); r.setAttribute('x', q[0]); r.setAttribute('y', q[1]); r.setAttribute('width', q[2]); r.setAttribute('height', q[3]); r.setAttribute('fill', '#ffd400'); ms[i].t.parentNode.insertBefore(r, ms[i].t); return r; });
    svg.getBoundingClientRect(); made[0].getBBox();
    const t5 = performance.now();
    await P.raf2();
    const t6 = performance.now();
    made.forEach(r => r.remove());
    return { n: ms.length, compute: +(t1 - t0).toFixed(1), layerInsertAndLayout: +(t2 - t1).toFixed(1), layerNextTwoFrames: +(t3 - t2).toFixed(1), perTextInsertAndLayout: +(t5 - t4).toFixed(1), perTextNextTwoFrames: +(t6 - t5).toFixed(1) };
  });
  console.log(engine, 'layer of', JSON.stringify(t));
  await b.close();
})();
