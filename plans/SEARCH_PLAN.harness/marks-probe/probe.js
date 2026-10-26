// Highlight-technique probe. Usage: node probe.js chromium|firefox|webkit [phases]
// Chromium: the playwright-core named in the brief (1.61.0, Chromium 149). Firefox and WebKit: that package expects
// firefox-1532 / webkit-2311, which are not installed, so they launch through the Microsoft.Playwright 1.59.0 driver's
// own playwright-core (1.59.1), whose bundled builds (firefox-1511, webkit-2272) are installed. No channel, no
// executablePath: bundled browsers only.
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const engine = process.argv[2] || 'chromium';
const phases = (process.argv[3] || 'a,b,d,e,f').split(',');
const PW_CHROMIUM = 'C:/Code/Kronikol4J/playwright/node_modules/playwright-core';
const PW_DRIVER = 'C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package';
const pw = require(engine === 'chromium' ? PW_CHROMIUM : PW_DRIVER);
const { PNG } = require(PW_CHROMIUM + '/lib/utilsBundle');

const HERE = __dirname;
const OUT = path.join(HERE, 'out', engine);
const RAW = path.join(OUT, 'raw');
fs.mkdirSync(RAW, { recursive: true });
const SHOTS = path.join(HERE, 'shots');
fs.mkdirSync(SHOTS, { recursive: true });

const PRED = {
  red: (r, g, b) => r >= 120 && r - g >= 60 && r - b >= 60,
  magenta: (r, g, b) => r >= 150 && b >= 150 && g <= 120 && (r + b) / 2 - g >= 80,
  green: (r, g, b) => g >= 110 && g - r >= 70 && g - b >= 70,
  blue: (r, g, b) => b - g >= 90 && b - r >= 90,
  orange: (r, g, b) => r >= 200 && g >= 100 && g <= 210 && r - b >= 100 && g - b >= 40,
};

function analyze(buf, clip, predName) {
  const png = PNG.sync.read(buf);
  const pred = PRED[predName];
  let n = 0, x1 = Infinity, y1 = Infinity, x2 = -Infinity, y2 = -Infinity;
  for (let y = 0; y < png.height; y++) for (let x = 0; x < png.width; x++) {
    const i = (y * png.width + x) * 4;
    if (pred(png.data[i], png.data[i + 1], png.data[i + 2])) { n++; if (x < x1) x1 = x; if (y < y1) y1 = y; if (x > x2) x2 = x; if (y > y2) y2 = y; }
  }
  return { n, bbox: n ? { x: clip.x + x1, y: clip.y + y1, w: x2 - x1 + 1, h: y2 - y1 + 1 } : null };
}

// pixels that changed between two screenshots of one clip, and how many of them now have the expected hue
function diff(bufA, bufB, predName) {
  const a = PNG.sync.read(bufA), b = PNG.sync.read(bufB);
  const pred = PRED[predName];
  let changed = 0, hue = 0;
  for (let i = 0; i < a.data.length; i += 4) {
    const d = Math.max(Math.abs(a.data[i] - b.data[i]), Math.abs(a.data[i + 1] - b.data[i + 1]), Math.abs(a.data[i + 2] - b.data[i + 2]));
    if (d > 40) { changed++; if (pred(b.data[i], b.data[i + 1], b.data[i + 2]) && !pred(a.data[i], a.data[i + 1], a.data[i + 2])) hue++; }
  }
  return { changed, hue };
}

const VW = 1400, VH = 1000;
function clipOf(rects, pad) {
  rects = rects.filter(r => r && r.w > 0 && r.h > 0);
  if (!rects.length) return null;
  let x1 = Math.min(...rects.map(r => r.x)) - pad, y1 = Math.min(...rects.map(r => r.y)) - pad;
  let x2 = Math.max(...rects.map(r => r.x + r.w)) + pad, y2 = Math.max(...rects.map(r => r.y + r.h)) + pad;
  x1 = Math.max(0, Math.floor(x1)); y1 = Math.max(0, Math.floor(y1)); x2 = Math.min(VW, Math.ceil(x2)); y2 = Math.min(VH, Math.ceil(y2));
  if (x2 - x1 < 2 || y2 - y1 < 2) return null;
  return { x: x1, y: y1, width: x2 - x1, height: y2 - y1 };
}
const near = (a, b, d) => a && b && Math.abs(a.x - b.x) < d && Math.abs(a.y - b.y) < d;
function deltas(r, ink) {
  if (!r || !ink) return null;
  const f = v => +v.toFixed(2);
  return { l: f(r.x - ink.x), r: f(r.x + r.w - (ink.x + ink.w)), t: f(r.y - ink.y), b: f(r.y + r.h - (ink.y + ink.h)) };
}

(async () => {
  const browser = await pw[engine].launch();
  const ctx = await browser.newContext({ viewport: { width: VW, height: VH }, deviceScaleFactor: 1 });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(String(e)));
  await page.goto(pathToFileURL(path.join(HERE, 'markup.html')).href, { waitUntil: 'load' });
  await page.waitForTimeout(1000);
  await page.addScriptTag({ path: path.join(HERE, 'probe-lib.js') });
  const res = { engine, version: browser.version(), ua: await page.evaluate(() => navigator.userAgent), hlSupported: await page.evaluate(() => P.hlSupported()), errors };
  console.log(engine, res.version, 'highlights:', res.hlSupported);

  async function shot(clip, file) {
    let last;
    for (let i = 0; i < 4; i++) {
      try { return await page.screenshot({ clip, path: file || undefined }); } catch (e) { last = e; await page.waitForTimeout(400); }
    }
    throw last;
  }
  const raf2 = () => page.evaluate(() => P.raf2());

  // ink truth: paint only the match red, screenshot, find the red pixels
  async function ink(id, clip, mode, tag) {
    await page.evaluate(([id, mode]) => { window._t = P.resolve(id); window._undo = P.inkPrep(window._t, mode); }, [id, mode || 'match']);
    await raf2();
    const buf = await shot(clip, tag ? path.join(RAW, tag + '.png') : null);
    await page.evaluate(() => { window._undo(); });
    await raf2();
    return analyze(buf, clip, 'red').bbox;
  }

  // ---------------- phase a: CSS Custom Highlight API ----------------
  if (phases.includes('a')) {
    // one property at a time; every ink colour is a pure green or magenta, which subpixel (LCD) fringes never produce
    const PROPS = {
      bg: ['background-color: rgb(255,0,255)', 'magenta'],
      color: ['color: rgb(0,170,0)', 'green'],
      fill: ['fill: rgb(0,170,0)', 'green'],
      wtfc: ['-webkit-text-fill-color: rgb(0,170,0)', 'green'],
      deco: ['text-decoration: underline rgb(0,170,0); text-decoration-thickness: 3px', 'green'],
      shadow: ['text-shadow: 3px 3px 0 rgb(0,170,0)', 'green'],
      stroke: ['stroke: rgb(0,170,0); stroke-width: 2px', 'green'],
      wts: ['-webkit-text-stroke: 1px rgb(0,170,0)', 'green'],
    };
    const TARGETS = ['ctl', 's0', 's1', 's2', 'm-json', 'm-step', 'm-link', 'm-cross', 'o-zwsp', 'o-box'];
    res.a = {};
    for (const id of TARGETS) {
      const m = await page.evaluate(id => { P.reset(); const t = P.resolve(id); P.show(t); return P.measure(t); }, id);
      await raf2();
      const m2 = await page.evaluate(id => P.measure(P.resolve(id)), id);
      const base = m2.kind === 'cross' ? P_union(m2.parts) : m2.kind === 'box' ? m2.R.u : m2.elBox;
      const clip = clipOf([base, near(m2.R.u, base, 60) ? m2.R.u : null], 8);
      const row = { clip, props: {} };
      const baseBuf = await shot(clip, path.join(RAW, `a-${id}-base.png`));
      for (const [k, [css, pred]] of Object.entries(PROPS)) {
        await page.evaluate(([id, css]) => { const t = P.resolve(id); P.setHL('k', css, [P.range(t)]); }, [id, css]);
        await raf2();
        const buf = await shot(clip, path.join(RAW, `a-${id}-${k}.png`));
        const dd = diff(baseBuf, buf, pred), after = analyze(buf, clip, pred);
        row.props[k] = { changed: dd.changed, hue: dd.hue, bbox: after.bbox };
      }
      // curated picture: background + color + fill together, wider clip
      await page.evaluate(id => { const t = P.resolve(id); P.setHL('k', 'background-color: rgb(255,0,255); color: rgb(0,170,0); fill: rgb(0,170,0)', [P.range(t)]); }, id);
      await raf2();
      const wide = clipOf([base], 40);
      await shot(wide, path.join(SHOTS, `${engine}-a-highlight-${id}.png`));
      await page.evaluate(() => P.setHL('k', '', []));
      // where the engine painted the background vs the matched glyphs
      const inkBox = await ink(id, clip, 'match');
      row.ink = inkBox;
      row.bgVsInk = row.props.bg.bbox ? deltas(row.props.bg.bbox, inkBox) : null;
      res.a[id] = row;
      console.log('a', id, Object.entries(row.props).map(([k, v]) => `${k}:${v.changed}/${v.hue}`).join(' '), 'bgVsInk', JSON.stringify(row.bgVsInk));
    }
  }

  // ---------------- phase b/c: rectangles vs the ink, under zoom ----------------
  if (phases.includes('b')) {
    const STATES = ['Z0', 'Zin', 'Zout', 'Ztf', 'Zvb', 'Zanc'];
    const TARGETS = ['ctl', 's0', 's1', 's2', 's3', 's4', 's5', 's6', 'm-json', 'm-part', 'm-step', 'm-link', 'm-cross', 'o-zwsp', 'o-box'];
    res.b = {};
    for (const st of STATES) {
      res.b[st] = {};
      for (const id of TARGETS) {
        const ok = await page.evaluate(([st, id]) => { const t = P.resolve(id); const ok = P.zoom(st, t); if (ok) P.show(t); return ok; }, [st, id]);
        if (!ok) continue;
        await raf2();
        const m = await page.evaluate(id => P.measure(P.resolve(id)), id);
        const base = m.kind === 'cross' ? P_union(m.parts) : m.kind === 'box' ? m.R.u : m.elBox;
        const clip = clipOf([base, near(m.R.u, base, 60) ? m.R.u : null, near(m.X, base, 60) ? m.X : null], 8);
        if (!clip) { res.b[st][id] = { offscreen: true, m }; continue; }
        const inkBox = await ink(id, clip, 'match', `b-${st}-${id}`);
        const row = {
          scale: m.ctm ? +Math.hypot(m.ctm[0], m.ctm[1]).toFixed(4) : null,
          ink: inkBox, Rn: m.R.n, R: deltas(m.R.u, inkBox), Rbb: deltas(m.R.bb, inkBox), X: deltas(m.X, inkBox), S: deltas(m.S, inkBox),
          Xerr: m.Xerr, Serr: m.Serr, raw: { R: m.R, X: m.X, S: m.S, Xu: m.Xu, Su: m.Su, compLen: m.compLen, bboxUser: m.bboxUser },
        };
        if (st === 'Z0') {
          row.perChar = m.perChar;
          if (m.kind === 'svg') {
            const a1 = await ink(id, clip, 'whole-style');
            const a2 = await ink(id, clip, 'whole-tspan');
            row.tspanCheck = deltas(a2, a1); // how far splitting into tspans moved the ink (should be ~0)
          }
          if (m.kind === 'cross') row.cross = { parts: m.parts, domAdjacent: m.domAdjacent, rects: m.R.list };
        }
        // curated picture: Range rects blue, getExtentOfChar red dashed, getSubStringLength green dashed
        if (['Z0', 'Ztf', 'Zanc', 'Zout'].includes(st)) {
          await page.evaluate(m => P.drawOutlines([{ r: m.R.u, color: '#0050ff' }, { r: m.X, color: '#e00000', dash: 1 }, { r: m.S, color: '#00a000', dash: 1 }]), m);
          await shot(clipOf([base], 30), path.join(SHOTS, `${engine}-bc-rects-${st}-${id}.png`));
          await page.evaluate(() => P.clearOutlines());
        }
        res.b[st][id] = row;
        console.log('b', st, id, 'scale', row.scale, 'R', JSON.stringify(row.R), 'X', JSON.stringify(row.X), 'S', JSON.stringify(row.S), row.tspanCheck ? 'tspan ' + JSON.stringify(row.tspanCheck) : '');
      }
    }
    await page.evaluate(() => P.reset());
  }

  // ---------------- phase d: the foreignObject box, deep match, scrolling ----------------
  if (phases.includes('d')) {
    res.d = {};
    for (const st of ['Z0', 'Zout', 'Ztf', 'Zanc']) {
      for (const method of ['mark', 'range', 'range-naive']) {
        const r = await page.evaluate(([st, method]) => { const t = P.resolve('o-deep'); P.zoom(st, t); return P.boxScroll(t, method); }, [st, method]);
        await raf2();
        // does a custom highlight paint on the deep match after the scroll?
        let hl = null;
        if (res.hlSupported) {
          const rr = await page.evaluate(() => { P.undoMark(); const t = P.resolve('o-deep'); P.setHL('k', 'background-color: rgb(255,0,255)', [P.range(t)]); return P.union(Array.prototype.map.call(P.range(t).getClientRects(), x => ({ x: x.left, y: x.top, w: x.width, h: x.height }))); });
          await raf2();
          const clip = clipOf([rr], 6);
          if (clip) { const a = analyze(await shot(clip), clip, 'magenta'); hl = { n: a.n, bboxVsRange: deltas(a.bbox, rr) }; }
          const box = await page.evaluate(() => { const d = document.querySelector('#d2 .note-scroll').getBoundingClientRect(); return { x: d.left, y: d.top, w: d.width, h: d.height }; });
          await shot(clipOf([box], 10), path.join(SHOTS, `${engine}-d-box-scroll-${method}-${st}.png`));
          await page.evaluate(() => P.setHL('k', '', []));
        } else { await page.evaluate(() => P.undoMark()); }
        r.hl = hl;
        res.d[`${st}/${method}`] = r;
        console.log('d', st, method, 'inBox', r.inBox, 'inWindow', r.inWindow, 'offCenter', r.offCenterPx && r.offCenterPx.toFixed(1), 'scrollTop', r.scrollTop && r.scrollTop.toFixed(1), 'hl', JSON.stringify(hl));
      }
    }
    await page.evaluate(() => P.reset());
  }

  // ---------------- phase e: <rect> in the SVG vs HTML overlay, across zoom and scroll ----------------
  if (phases.includes('e')) {
    res.e = {};
    for (const id of ['m-json', 's1', 'o-zwsp', 'o-box']) {
      res.e[id] = {};
      const seq = ['Z0', 'scroll', 'Zin', 'Zout', 'Ztf', 'Zvb', 'Zanc'].concat(id === 'o-box' ? ['boxScroll'] : []).concat(['ZinPan']);
      for (const st of seq) {
        // place at the starting state, then change it
        const placeAt = st === 'ZinPan' ? 'Zin' : 'Z0';
        await page.evaluate(([id, placeAt]) => { const t = P.resolve(id); P.zoom(placeAt, t); P.show(t); }, [id, placeAt]);
        await raf2();
        await page.evaluate(id => P.placeOverlays(P.resolve(id)), id);
        await raf2();
        await page.evaluate(([id, st]) => {
          const t = P.resolve(id);
          if (st === 'scroll') window.scrollBy(0, 137);
          else if (st === 'boxScroll') t.el.scrollTop = 34;
          else if (st === 'ZinPan') { const c = t.svg.parentElement; c.scrollLeft = c.scrollLeft + 150; }
          else if (st !== 'Z0') {
            // change zoom without resetting the overlays: P.zoom resets the svg first, which leaves the overlays alone
            P.zoom(st, t); P.show(t);
          }
        }, [id, st]);
        await raf2();
        const m = await page.evaluate(id => ({ m: P.measure(P.resolve(id)), ov: P.overlayRects() }), id);
        const base = m.m.kind === 'box' ? m.m.R.u : m.m.elBox;
        const clip = clipOf([base], 8);
        let inkBox = null;
        if (clip) {
          await page.evaluate(() => P.overlayVisible(false));
          inkBox = await ink(id, clip, 'match');
          await page.evaluate(() => P.overlayVisible(true));
        }
        const center = (r) => r ? { x: r.x + r.w / 2, y: r.y + r.h / 2 } : null;
        const off = (r) => { const a = center(r), b = center(inkBox); return a && b ? { dx: +(a.x - b.x).toFixed(1), dy: +(a.y - b.y).toFixed(1), wRatio: +(r.w / inkBox.w).toFixed(2) } : null; };
        res.e[id][st] = { svgRect: off(m.ov.svgRect), html: off(m.ov.html), freshRange: off(m.m.R.u) };
        await raf2();
        await shot(clipOf([base], 40), path.join(SHOTS, `${engine}-e-overlay-${id}-${st}.png`));
        console.log('e', id, st, JSON.stringify(res.e[id][st]));
        await page.evaluate(() => { P.clearOverlays(); P.reset(); });
      }
    }
  }

  // ---------------- phase g: many matches, a multiply layer vs the highlight API ----------------
  if (phases.includes('g')) {
    await page.evaluate(() => P.reset());
    const region = await page.evaluate(() => {
      const svg = document.querySelector(P.roots.d1 + ' svg');
      const texts = [...svg.querySelectorAll('text')], ms = [];
      texts.forEach(t => { let i = -1; while ((i = t.textContent.indexOf('Some_', i + 1)) >= 0) ms.push({ t, s: i, e: i + 5 }); });
      window._ms = ms;
      // the POST /muffins request note: the first text holding '"milk": "Some_Fresh_Milk",'
      const first = ms.find(m => m.t.textContent === '"Some_Fresh_Milk",');
      first.t.scrollIntoView({ block: 'center' });
      const g = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      g.setAttribute('style', 'mix-blend-mode: multiply; pointer-events: none'); g.setAttribute('class', 'probe-ov');
      ms.forEach(m => { const u = P.extentUser(m.t, m.s, m.e); const r = document.createElementNS('http://www.w3.org/2000/svg', 'rect'); r.setAttribute('x', u.x); r.setAttribute('y', u.y); r.setAttribute('width', u.w); r.setAttribute('height', u.h); r.setAttribute('fill', '#ffd400'); g.appendChild(r); });
      svg.appendChild(g);
      const b = first.t.getBoundingClientRect();
      return { x: b.left - 20, y: b.top - 30, w: 360, h: 140, n: ms.length };
    });
    await raf2();
    const clip = clipOf([region], 0);
    await shot(clip, path.join(SHOTS, `${engine}-g-multiply-layer-Some_.png`));
    await page.evaluate(() => { P.clearOverlays(); P.setHL('k', 'background-color: #ffd400', window._ms.map(m => { const r = document.createRange(); r.setStart(m.t.firstChild, m.s); r.setEnd(m.t.firstChild, m.e); return r; })); });
    await raf2();
    await shot(clip, path.join(SHOTS, `${engine}-g-highlight-api-Some_.png`));
    await page.evaluate(() => P.setHL('k', '', []));
    console.log('g', region.n, 'matches drawn');
  }

  // ---------------- phase f: timing ----------------
  if (phases.includes('f')) {
    await page.evaluate(() => P.reset());
    res.f = {
      fifty: await page.evaluate(() => P.timing('Some_|Apple|Granny', '50 matches: Some_|Apple|Granny')),
      many: await page.evaluate(() => P.timing('"', 'every double quote')),
      box: await page.evaluate(() => P.boxTiming('"$ref"', 50)),
    };
    console.log('f', JSON.stringify(res.f));
  }

  fs.writeFileSync(path.join(OUT, 'results.json'), JSON.stringify(res, null, 1));
  console.log('page errors:', errors.length ? errors : 'none');
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });

function P_union(rs) {
  rs = rs.filter(r => r && (r.w > 0 || r.h > 0));
  if (!rs.length) return null;
  const x1 = Math.min(...rs.map(r => r.x)), y1 = Math.min(...rs.map(r => r.y));
  const x2 = Math.max(...rs.map(r => r.x + r.w)), y2 = Math.max(...rs.map(r => r.y + r.h));
  return { x: x1, y: y1, w: x2 - x1, h: y2 - y1 };
}
