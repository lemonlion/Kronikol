'use strict';
// #227: does a toggle move a zoomed diagram off the screen? Opens a fixture report, draws a run of scenarios, zooms one
// scenario's diagram (or leaves it at fit), stands it in the window, clicks a report toggle in the pinned bar with the
// mouse (a real click: the button takes focus), and samples the window's scroll and the diagram's place every frame
// until the page has been still for a second.
// usage: node jump.js <fixture-dir> <toggle> <zoom> [where] [scenario] [vw] [vh]
//   toggle   headers | steps | assertions | databases | events | setup
//   zoom     in (the slider halfway up its range) | max (100) | none (the fit level)
//   where    mid (the diagram across the window's middle, default) | top (its top 80 px below the bar) | below-mid
//   scenario index of the scenario among details.scenario (default 8)
// Chromium only, the bundled one (never a channel: CLAUDE.md). PLAYWRIGHT_CORE picks another playwright-core.
const fs = require('fs');
const path = require('path');
// BROWSER=firefox|webkit: playwright-core 1.59.1 from the .NET suite's package, whose Firefox and WebKit are installed
const ENGINE = process.env.BROWSER || 'chromium';
const chromium = require(process.env.PLAYWRIGHT_CORE || (ENGINE === 'chromium' ? 'C:/Code/Kronikol4J/playwright/node_modules/playwright-core'
  : 'C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package'))[ENGINE];

const [dir, toggle = 'headers', zoom = 'in', where = 'mid', si = '8', vw = '1366', vh = '768'] = process.argv.slice(2);
if (!dir) { console.error('usage: node jump.js <fixture-dir> <toggle> <zoom> [where] [scenario] [vw] [vh]'); process.exit(2); }

(async () => {
  const entry = fs.readFileSync(path.join(dir, 'ENTRY.txt'), 'utf8').trim();
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: +vw, height: +vh } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  await page.goto('file:///' + path.resolve(dir, entry).replace(/\\/g, '/'));
  await page.waitForSelector('details.feature', { timeout: 60000 });

  // every scenario open and drawn, as a reader who has read down the report has them
  await page.evaluate(() => {
    document.querySelectorAll('details.feature').forEach((f) => { f.open = true; });
    document.querySelectorAll('details.scenario').forEach((s) => {
      s.open = true;
      if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(s);
    });
  });
  await page.waitForFunction(() => {
    const boxes = Array.from(document.querySelectorAll('details.scenario [data-diagram-type="plantuml"]'));
    return boxes.length && boxes.every((c) => {
      const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
      return parts.length ? parts.every((p) => p.querySelector(':scope > svg')) : !!c.querySelector(':scope > svg');
    }) && !window._plantumlRendering;
  }, null, { timeout: 120000, polling: 200 });

  // ZOOM_ALSO=1,2: those scenarios' diagrams zoomed in too (halfway up their range) before the one read is placed
  for (const other of (process.env.ZOOM_ALSO || '').split(',').filter(Boolean).map(Number)) {
    await page.evaluate(async (oi) => {
      const frames = (n) => new Promise((done) => { const t = () => (--n <= 0 ? setTimeout(done, 0) : requestAnimationFrame(t)); requestAnimationFrame(t); });
      const c = document.querySelectorAll('details.scenario')[oi].querySelector('[data-diagram-type="plantuml"]');
      c.scrollIntoView({ block: 'center' });
      for (let i = 0; i < 100 && !c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider'); i++) await frames(2);
      const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
      s.value = String(Math.round((parseInt(s.min) + 100) / 2));
      s.dispatchEvent(new Event('input', { bubbles: true }));
      await frames(3);
    }, other);
  }

  // the scenario's diagram brought to the screen, given its slider, and zoomed
  const info = await page.evaluate(async ({ si, zoom, where, scope }) => {
    const frames = (n) => new Promise((done) => { const t = () => (--n <= 0 ? setTimeout(done, 0) : requestAnimationFrame(t)); requestAnimationFrame(t); });
    const sc = document.querySelectorAll('details.scenario')[si];
    const c = sc.querySelector('[data-diagram-type="plantuml"]');
    c.setAttribute('data-probe', '1');
    c.scrollIntoView({ block: 'center' });
    for (let i = 0; i < 100 && !c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider'); i++) await frames(2);
    const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
    if (!s) return { error: 'no slider' };
    if (zoom !== 'none') {
      s.value = zoom === 'max' ? '100' : String(Math.round((parseInt(s.min) + 100) / 2));
      s.dispatchEvent(new Event('input', { bubbles: true }));
      await frames(3);
    }
    // SCOPE=scenario: the scenario's own settings opened (its gear), so that its own toggle is clicked
    if (scope === 'scenario') { const g = sc.querySelector('.scenario-settings-gear'); if (g) { g.click(); await frames(4); } }
    const nav = document.querySelector('.toolbar-row.report-nav');
    // the diagram drawn before this one in the page (the scenario above's), whose redraw moves this one
    const all = Array.from(document.querySelectorAll('[data-diagram-type="plantuml"]'));
    const prev = all[all.indexOf(c) - 1] || null;
    // placed again until it holds: the page lays out late around a scroll (content-visibility), most in Firefox and WebKit
    const off = () => {
      const r = c.getBoundingClientRect(), barBottom = nav ? nav.getBoundingClientRect().bottom : 0;
      if (where === 'top') return r.top - (barBottom + 80);
      if (where === 'below-mid') return r.top - window.innerHeight * 0.6;
      // deep:<px>: the window's middle <px> down the diagram
      if (/^deep:\d+$/.test(where)) return r.top + parseInt(where.slice(5), 10) - window.innerHeight / 2;
      // gap: the window's top edge 20 px below the diagram above (in its scenario's foot, not cutting the picture)
      if (where === 'gap' && prev) return prev.getBoundingClientRect().bottom + 20;
      if (where === 'cut' && prev) return prev.getBoundingClientRect().bottom - 40;
      return r.top + Math.min(r.height, window.innerHeight) / 2 - window.innerHeight / 2;
    };
    if ((where === 'gap' || where === 'cut') && prev) { prev.scrollIntoView({ block: 'end' }); await frames(4); }
    const consistent = () => !prev || !(where === 'gap' || where === 'cut') || prev.getBoundingClientRect().bottom <= c.getBoundingClientRect().top;
    for (let i = 0; i < 12; i++) {
      const d = off();
      if (Math.abs(d) <= 1 && i > 0 && consistent()) break;
      window.scrollBy(0, d);
      await frames(4);
    }
    window.__landmarkOf = () => {
      const texts = Array.from(c.querySelectorAll('svg text'));
      return texts;
    };
    {
      // at the reading line (the plan's Q1 (a)): the middle of the window below the bar and any stuck settings bar
      const below = document.elementFromPoint(window.innerWidth / 2, window.innerHeight / 2) || document.body;
      let free = 0;
      try { free = window.kronikol_freeTop ? window.kronikol_freeTop(below) : 0; } catch (e) { free = 0; }
      const mid = (free + window.innerHeight) / 2, texts = window.__landmarkOf();
      let best = -1, bd = Infinity;
      texts.forEach((t, i) => { const r = t.getBoundingClientRect(); if (!r.height) return;
        const d = Math.abs((r.top + r.bottom) / 2 - mid); if (d < bd) { bd = d; best = i; } });
      if (best >= 0) {
        const words = texts[best].textContent, k = texts.slice(0, best).filter((t) => t.textContent === words).length;
        const r = texts[best].getBoundingClientRect();
        window.__landmark = { words, k, y0: Math.round((r.top + r.bottom) / 2), line: Math.round(mid) };
        // the nearest text whose words occur once in the diagram: the measure no other drawing of the same words can fool
        window.__unique = null;
        { let ub = Infinity; texts.forEach((t, i) => { const q = t.getBoundingClientRect(); if (!q.height) return;
          const w = t.textContent; if (texts.filter((u) => u.textContent === w).length !== 1) return;
          const d = Math.abs((q.top + q.bottom) / 2 - mid); if (d < ub) { ub = d; window.__unique = { w, y0: (q.top + q.bottom) / 2, d: Math.round(d) }; } }); }
        window.__near = [];
        texts.forEach((t, i) => { const q = t.getBoundingClientRect(); const cy = (q.top + q.bottom) / 2;
          if (!q.height || Math.abs(cy - mid) > 80) return;
          const w = t.textContent; window.__near.push({ w, k: texts.slice(0, i).filter((u) => u.textContent === w).length, y0: cy }); });
      }
    }
    const r2 = c.getBoundingClientRect();
    const pr = prev ? prev.getBoundingClientRect() : null;
    const edge = document.elementsFromPoint(window.innerWidth / 2, 2).filter((e) => !(nav && nav.contains(e)) && e !== document.documentElement && e !== document.body)
      .slice(0, 3).map((e) => e.tagName + '.' + String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : e.className).slice(0, 30));
    return { min: s.min, value: s.value, zoomed: c.classList.contains('diagram-natural-size'), cls: c.className,
      parts: c.querySelectorAll(':scope > .puml-fragment').length, cw: c.clientWidth,
      w: Array.from(c.querySelectorAll('svg')).slice(0, 4).map((v) => Math.round(v.getBoundingClientRect().width) + '/' + Math.round(v.width.baseVal.value)),
      height: Math.round(r2.height),
      top: Math.round(r2.top), scrollY: Math.round(window.scrollY), docH: document.documentElement.scrollHeight,
      floating: nav ? nav.classList.contains('k-floating') : null, prev: pr ? [Math.round(pr.top), Math.round(pr.bottom)] : null, edge };
  }, { si: +si, zoom, where, scope: process.env.SCOPE || 'report' });
  if (info.error) { console.log(JSON.stringify(info)); await browser.close(); process.exit(1); }

  // the button in the pinned bar, clicked with the mouse
  // the report's own toggle (not a scenario's), wherever the bar shows it at this width
  const btn = process.env.SCOPE === 'scenario'
    ? page.locator('details.scenario').nth(+si).locator(`.diagram-options-bar button[data-toggle='${toggle}']:visible`).first()
    : page.locator(`button[data-toggle='${toggle}'][onclick^='window._toggle']:not([onclick*='Scenario']):visible`).first();
  if (toggle !== 'note' && !(await btn.count())) { console.log('no report toggle', toggle); await browser.close(); process.exit(1); }
  await page.evaluate((on) => { window.__probeRects = on[0]; window.__probeTrace = on[1]; }, [process.env.PROBE_RECTS === '1', process.env.TRACE === '1']);
  // PROTO=1: the plan's place keeper, prototyped in probe/proto.js, put in before the click (PROTO_OFF turns pieces off)
  if (process.env.PROTO === '1') await page.evaluate((src) => { window.__protoOff = src.off; window.__protoMode = src.mode; (0, eval)(src.code); },
    { code: fs.readFileSync(path.join(__dirname, 'proto.js'), 'utf8'), off: process.env.PROTO_OFF || '', mode: process.env.PROTO_MODE || 'fraction' });
  await page.evaluate(() => {
    // every write to the window's scroll from here on, with who made it (PROBE_WRITES=0 leaves them alone)
    window.__writes = [];
    const t00 = performance.now();
    const log = (what, args) => window.__writes.push({ t: Math.round(performance.now() - t00), what, args: JSON.stringify(args).slice(0, 80),
      y: Math.round(window.scrollY), stack: (new Error().stack || '').split('\n').slice(2, 7).map((l) => l.trim().replace(/\(?file:.*?([^/\\]+\.html)?:(\d+):(\d+)\)?/, '@$2:$3')).join(' < ') });
    for (const name of ['scrollBy', 'scrollTo', 'scroll']) {
      const orig = window[name];
      window[name] = function (...a) { log(name, a); return orig.apply(this, a); };
    }
    const siv = Element.prototype.scrollIntoView;
    Element.prototype.scrollIntoView = function (...a) { log('scrollIntoView ' + this.tagName + '.' + this.className, a); return siv.apply(this, a); };
    const d = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollTop');
    Object.defineProperty(Element.prototype, 'scrollTop', { configurable: true, get() { return d.get.call(this); },
      set(v) { if (this === document.documentElement || this === document.body) log('scrollTop= ' + this.tagName, [v]); d.set.call(this, v); } });
    // PROBE_RECTS=1: what the zoom code reads of the picture and the box while it keeps the reader's place
    if (window.__probeRects) {
      const gbcr = Element.prototype.getBoundingClientRect;
      Element.prototype.getBoundingClientRect = function () {
        const r = gbcr.call(this);
        const st = new Error().stack || '';
        const m = /at (placeBefore|keepPlace|resizes|addZoomButton|zoomFacts|naturalWidthOf)/.exec(st);
        if (m && window.__writes.length < 60) window.__writes.push({ t: Math.round(performance.now() - t00), what: 'rect ' + m[1] + ' ' + this.tagName,
          args: [Math.round(r.top), Math.round(r.height), Math.round(r.width)].join(','), y: Math.round(window.scrollY),
          stack: 'svgstyle w=' + (this.style ? this.style.width : '') + ' mw=' + (this.style ? this.style.maxWidth : '') });
        return r;
      };
    }
    // TRACE=1: each swap (window._zoomState.give, which both swaps call right after the new picture is written) and each
    // zoom restore, with the box's scenario, the window's scroll and the read diagram's top before and after, and whether
    // the box's scenario is skipped by content-visibility
    if (window.__probeTrace) {
      const probe = document.querySelector('[data-probe]');
      const scen = Array.from(document.querySelectorAll('details.scenario'));
      const idx = (box) => scen.findIndex((x) => x.contains(box));
      const skipped = (box) => { const x = box.closest('details.scenario');
        try { return x && x.checkVisibility ? !x.checkVisibility({ contentVisibilityAuto: true }) : 'n/a'; } catch (e) { return 'n/a'; } };
      const at = (box) => { const b = box.getBoundingClientRect(), p = probe.getBoundingClientRect();
        return { y: Math.round(window.scrollY), top: Math.round(b.top), h: Math.round(b.height), probe: Math.round(p.top) }; };
      const push = (what, a, b, extra) => window.__writes.push({ t: Math.round(performance.now() - t00), what,
        args: 'box ' + a.top + '/' + a.h + ' -> ' + b.top + '/' + b.h, y: a.y + ' -> ' + b.y, stack: 'probe ' + a.probe + ' -> ' + b.probe + extra });
      const give = window._zoomState.give;
      window._zoomState.give = function (box, state) {
        const sk = skipped(box), a = at(box);
        const out = give.apply(this, arguments);
        push('swap s' + idx(box) + (state && state.zoomed ? ' zoomed' : ''), a, at(box), ' skipped ' + sk);
        return out;
      };
      const rz = window._restoreZoomState;
      window._restoreZoomState = function (box) {
        const sk = skipped(box), a = at(box);
        const out = rz.apply(this, arguments);
        push('restore s' + idx(box), a, at(box), ' skipped ' + sk);
        return out;
      };
    }
    window.addEventListener('scroll', () => window.__writes.push({ t: Math.round(performance.now() - t00), what: 'scroll event', y: Math.round(window.scrollY) }), { passive: true });
  });
  await page.evaluate(() => {
    const c = document.querySelector('[data-probe]');
    window.__samples = [];
    const t0 = performance.now();
    let last = null, still = 0;
    const tick = () => {
      const r = c.getBoundingClientRect();
      const s = { t: Math.round(performance.now() - t0), y: Math.round(window.scrollY), top: Math.round(r.top), h: Math.round(r.height),
        anchor: getComputedStyle(document.documentElement).overflowAnchor === 'none' ? 0 : 1, focus: document.activeElement ? document.activeElement.tagName + '.' + (document.activeElement.getAttribute('data-toggle') || '') : '' };
      window.__samples.push(s);
      const key = s.y + '|' + s.top + '|' + s.h;
      still = key === last ? still + 1 : 0;
      last = key;
      if (still < 60 && s.t < 15000) requestAnimationFrame(tick); else window.__done = true;
    };
    requestAnimationFrame(tick);
  });
  // CLICK=js: the button's own click() (no focus moves); CLICK=blur: a mouse click, then the focus taken off it at once
  // toggle=note: a note's own expand button in the diagram read, the one nearest the window's middle, clicked by a
  // dispatched event, as the E2E suite clicks SVG (its hover-only work given first, as a pointer over it would)
  if (toggle === 'note') {
    const hit = await page.evaluate(async (process_note_cls) => {
      const c = document.querySelector('[data-probe]');
      const frames = (n) => new Promise((done) => { const t = () => (--n <= 0 ? setTimeout(done, 0) : requestAnimationFrame(t)); requestAnimationFrame(t); });
      c.querySelector('svg').dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
      await frames(3);
      const mid = window.innerHeight / 2;
      // the note nearest the window's middle, double-clicked (the suite's way to cycle a note's state)
      let best = null, bd = Infinity;
      for (const h of c.querySelectorAll('.note-hover-rect')) { const r = h.getBoundingClientRect(); if (!r.height || r.top < 0 || r.bottom > window.innerHeight) continue;
        const d = Math.abs((r.top + r.bottom) / 2 - mid); if (d < bd) { bd = d; best = h; } }
      if (!best) return null;
      const q = best.getBoundingClientRect();
      best.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, clientX: q.left + q.width / 2, clientY: q.top + q.height * 0.75 }));
      return { cls: best.getAttribute('class'), y: Math.round(q.top) };
    }, process.env.NOTE_CLS || '');
    if (!hit) { console.log('no note button on screen'); await browser.close(); process.exit(1); }
  } else if (process.env.CLICK === 'js') await btn.evaluate((b) => b.click());
  else { await btn.click(); if (process.env.CLICK === 'blur') await btn.evaluate((b) => b.blur()); }
  await page.waitForFunction(() => window.__done, null, { timeout: 30000, polling: 200 });
  const samples = await page.evaluate(() => window.__samples);
  const landmark = await page.evaluate(() => {
    const l = window.__landmark;
    if (!l) return null;
    const same = window.__landmarkOf().filter((t) => t.textContent === l.words);
    const t = same[l.k];
    if (!t) return Object.assign({ lost: true }, l);
    const r = t.getBoundingClientRect();
    const all = window.__landmarkOf(), moves = [];
    for (const n of window.__near || []) { const u = all.filter((x) => x.textContent === n.w)[n.k]; if (u) { const q = u.getBoundingClientRect(); moves.push((q.top + q.bottom) / 2 - n.y0); } }
    moves.sort((a, b) => a - b);
    const near = moves.length ? { n: moves.length, median: Math.round(moves[moves.length >> 1]), min: Math.round(moves[0]), max: Math.round(moves[moves.length - 1]) } : null;
    const uq = window.__unique, ut = uq ? all.filter((x) => x.textContent === uq.w) : [];
    const unique = uq ? (ut.length === 1 ? { words: uq.w, d: uq.d, moved: Math.round((ut[0].getBoundingClientRect().top + ut[0].getBoundingClientRect().bottom) / 2 - uq.y0) } : { words: uq.w, lost: ut.length }) : null;
    return Object.assign({ y1: Math.round((r.top + r.bottom) / 2), moved: Math.round((r.top + r.bottom) / 2) - l.y0, near, unique }, l);
  });
  const first = samples[0], lastS = samples[samples.length - 1];
  const changes = [];
  for (let i = 1; i < samples.length; i++) {
    const a = samples[i - 1], b = samples[i];
    if (a.y !== b.y || a.top !== b.top || a.h !== b.h) changes.push(b);
  }
  const onScreen = lastS.top < +vh && lastS.top + lastS.h > 0;
  console.log(JSON.stringify({ toggle, zoom, where, si: +si, before: info,
    after: { top: lastS.top, h: lastS.h, y: lastS.y, onScreen, movedBy: lastS.top - first.top },
    landmark,
    proto: await page.evaluate(() => window.__protoLog || null),
    worst: samples.reduce((m, s) => (Math.abs(s.top - first.top) > Math.abs(m.top - first.top) ? s : m), first),
    changes: changes.slice(0, 40), errors, writes: await page.evaluate((n) => window.__writes.slice(0, n), process.env.TRACE === '1' ? 120 : 30) }));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
