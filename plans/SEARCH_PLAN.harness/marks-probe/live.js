// The key checks again on the live demo report, where each engine draws its own diagrams.
// Usage: node live.js chromium|firefox|webkit
const fs = require('fs');
const path = require('path');
const engine = process.argv[2] || 'chromium';
const PW_CHROMIUM = 'C:/Code/Kronikol4J/playwright/node_modules/playwright-core';
const pw = require(engine === 'chromium' ? PW_CHROMIUM : 'C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package');
const { PNG } = require(PW_CHROMIUM + '/lib/utilsBundle');
const URL = 'https://lemonlion.github.io/BreakfastProvider/reports/reqnroll/TestRunReport.html';
const SC = { muffin: 'scenario-a-valid-apple-cinnamon-muffin-request-should-return-a-fresh-batch', openapi: 'scenario-the-openapi-endpoint-should-return-a-valid-specification' };
const SHOTS = path.join(__dirname, 'shots');
const pred = {
  red: (r, g, b) => r >= 120 && r - g >= 60 && r - b >= 60,
  magenta: (r, g, b) => r >= 150 && b >= 150 && g <= 120 && (r + b) / 2 - g >= 80,
};
function count(buf, clip, p) {
  const png = PNG.sync.read(buf); let n = 0, x1 = 1e9, y1 = 1e9, x2 = -1e9, y2 = -1e9;
  for (let y = 0; y < png.height; y++) for (let x = 0; x < png.width; x++) {
    const i = (y * png.width + x) * 4;
    if (pred[p](png.data[i], png.data[i + 1], png.data[i + 2])) { n++; x1 = Math.min(x1, x); y1 = Math.min(y1, y); x2 = Math.max(x2, x); y2 = Math.max(y2, y); }
  }
  return { n, bbox: n ? { x: clip.x + x1, y: clip.y + y1, w: x2 - x1 + 1, h: y2 - y1 + 1 } : null };
}
const clipOf = (r, pad) => r ? { x: Math.max(0, Math.floor(r.x - pad)), y: Math.max(0, Math.floor(r.y - pad)), width: Math.ceil(r.w + 2 * pad), height: Math.ceil(r.h + 2 * pad) } : null;
const d = (r, ink) => r && ink ? { l: +(r.x - ink.x).toFixed(1), r: +(r.x + r.w - ink.x - ink.w).toFixed(1), t: +(r.y - ink.y).toFixed(1), b: +(r.y + r.h - ink.y - ink.h).toFixed(1) } : null;

(async () => {
  const b = await pw[engine].launch();
  const page = await (await b.newContext({ viewport: { width: 1400, height: 1000 } })).newPage();
  const t0 = Date.now();
  await page.goto(URL, { waitUntil: 'load', timeout: 180000 });
  await page.evaluate(() => {
    const sel = [...document.querySelectorAll('select.note-format-select')].find(s => !s.closest('details.feature'));
    sel.value = [...sel.options].find(o => /json/i.test(o.textContent)).value;
    sel.dispatchEvent(new Event('change', { bubbles: true }));
    [...document.querySelectorAll('button.details-radio-btn')].find(x => !x.closest('details.feature') && /Expanded/.test(x.textContent)).click();
  });
  const ids = {};
  for (const [k, id] of Object.entries(SC)) {
    await page.evaluate((id) => { const sc = document.getElementById(id); for (let e = sc; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true; sc.scrollIntoView(); }, id);
    await page.waitForSelector(`#${id} .diagram-view-seq .plantuml-browser > svg`, { timeout: 180000 });
    ids[k] = await page.evaluate((id) => document.querySelector(`#${id} .diagram-view-seq .plantuml-browser`).id, id);
  }
  await page.waitForSelector(`#${ids.openapi} svg foreignObject .note-scroll`, { timeout: 120000 });
  await page.evaluate((c) => document.querySelector(`#${c} svg foreignObject`).scrollIntoView({ block: 'center' }), ids.openapi);
  await page.waitForTimeout(1500);
  const already = await page.evaluate(() => typeof window.P);
  await page.addScriptTag({ path: path.join(__dirname, 'probe-lib.js') });
  await page.evaluate((ids) => { P.roots = { d1: '#' + ids.muffin, d2: '#' + ids.openapi }; }, ids);
  const res = { engine, version: b.version(), drawnInSec: (Date.now() - t0) / 1000, ids, globalPBefore: already };
  const raf2 = () => page.evaluate(() => P.raf2());

  for (const id of ['m-json', 'm-cross', 'o-zwsp', 'o-box']) {
    const m = await page.evaluate((id) => { const t = P.resolve(id); if (!t) return null; if (t.kind === 'box') { t.fo.scrollIntoView({ block: 'start' }); window.scrollBy(0, -200); } else t.el.scrollIntoView({ block: 'center' }); return true; }, id);
    if (!m) { res[id] = 'target not found'; continue; }
    await raf2();
    if (id === 'o-box') await page.waitForFunction(() => { const d = document.querySelector(P.roots.d2 + ' .note-scroll'); return d && d.textContent.indexOf('BreakfastProvider.Api') >= 0; }, null, { timeout: 30000, polling: 200 });
    const mm = await page.evaluate((id) => { const t = P.resolve(id); const m = P.measure(t); m.scale = t.svg ? t.svg.getBoundingClientRect().width / t.svg.viewBox.baseVal.width : null; m.text = t.kind === 'box' ? null : t.el.textContent; return m; }, id);
    const base = mm.kind === 'box' ? mm.R.u : mm.kind === 'cross' ? P_union(mm.parts) : mm.elBox;
    const clip = clipOf(base, 10);
    // ink
    await page.evaluate((id) => { window._t = P.resolve(id); window._u = P.inkPrep(window._t, 'match'); }, id);
    await raf2();
    const ink = count(await page.screenshot({ clip }), clip, 'red').bbox;
    await page.evaluate(() => window._u());
    await raf2();
    // custom highlight background
    await page.evaluate((id) => { const t = P.resolve(id); P.setHL('k', 'background-color: rgb(255,0,255); color: rgb(0,170,0)', [P.range(t)]); }, id);
    await raf2();
    const hl = count(await page.screenshot({ clip }), clip, 'magenta');
    // a <rect> from getExtentOfChar in user space, behind the text
    await page.evaluate((id) => {
      const t = P.resolve(id); if (t.kind !== 'svg') return;
      const u = P.extentUser(t.el, t.s, t.e), r = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
      r.setAttribute('x', u.x); r.setAttribute('y', u.y); r.setAttribute('width', u.w); r.setAttribute('height', u.h);
      r.setAttribute('fill', '#ffd400'); r.setAttribute('class', 'probe-ov'); t.el.parentNode.insertBefore(r, t.el);
    }, id);
    await raf2();
    const wide = clipOf(base, 36);
    await page.screenshot({ clip: wide, path: path.join(SHOTS, `${engine}-live-${id}.png`) });
    await page.evaluate(() => { P.setHL('k', '', []); P.clearOverlays(); });
    res[id] = { scale: mm.scale && +mm.scale.toFixed(4), text: mm.text, Rn: mm.R.n, R: d(mm.R.u, ink), X: d(mm.X, ink), S: d(mm.S, ink), hlPixels: hl.n, hlVsInk: d(hl.bbox, ink) };
    console.log(engine, 'live', id, JSON.stringify(res[id]));
  }

  // the box after a report-like zoom out (the report sets the picture's CSS width)
  await page.evaluate((c) => { const svg = document.querySelector(`#${c} > svg`); svg.style.maxWidth = '100%'; svg.style.width = (svg.viewBox.baseVal.width * 0.5) + 'px'; svg.querySelector('foreignObject').scrollIntoView({ block: 'start' }); window.scrollBy(0, -60); }, ids.openapi);
  await raf2(); await page.waitForTimeout(300);
  const boxR = await page.evaluate((c) => { const r = document.querySelector(`#${c} svg foreignObject`).getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; }, ids.openapi);
  await page.screenshot({ clip: clipOf(boxR, 30), path: path.join(SHOTS, `${engine}-live-box-zoom50.png`) });
  await page.evaluate((c) => { const svg = document.querySelector(`#${c} > svg`); svg.style.width = ''; }, ids.openapi);

  // deep match in the box: <mark>.scrollIntoView vs range arithmetic, at the report's own size
  for (const method of ['mark', 'range']) {
    const r = await page.evaluate((method) => { const t = P.resolve('o-deep'); const r = P.boxScroll(t, method); P.undoMark(); return r; }, method);
    res['deep-' + method] = { inBox: r.inBox, inWindow: r.inWindow, offCenterPx: r.offCenterPx && +r.offCenterPx.toFixed(1) };
    console.log(engine, 'live deep', method, JSON.stringify(res['deep-' + method]));
  }
  fs.writeFileSync(path.join(__dirname, 'out', `live-${engine}.json`), JSON.stringify(res, null, 1));
  await b.close();
})().catch(e => { console.error(e); process.exit(1); });

function P_union(rs) {
  rs = rs.filter(r => r && (r.w > 0 || r.h > 0));
  if (!rs.length) return null;
  const x1 = Math.min(...rs.map(r => r.x)), y1 = Math.min(...rs.map(r => r.y));
  return { x: x1, y: y1, w: Math.max(...rs.map(r => r.x + r.w)) - x1, h: Math.max(...rs.map(r => r.y + r.h)) - y1 };
}
