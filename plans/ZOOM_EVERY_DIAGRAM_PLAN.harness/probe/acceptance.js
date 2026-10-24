'use strict';
// Section 7.4's acceptance on a real report (the 2026-10-24 audit of 5.7.0 to 5.7.2): the report with this worktree's
// page scripts swapped in (patch-report.js), at the window sizes 7.4 names. Nothing here is a timing; the timed
// clauses (a notch on scenario 278 within 50 ms, the toggle bench) are run apart, as a person must not use the
// machine meanwhile.
// usage: node acceptance.js <report.html> <vw> <vh> <stage> [scenarioIndex]
//   sliders            every diagram drawn, each brought to the screen: a slider on every laid-out diagram, none on a
//                      part, every part of a diagram at one level, the floor (five diagrams that fit), every diagram
//                      in parts lined up, and no stored source holding a line the page adds
//   scenario <si>      one diagram in parts: one level, Ctrl with the wheel over each part prevented and zooming every
//                      part, a click ringing the whole diagram, the SVG and PNG copies whole, one footbox
//   point <si>         one notch keeps the point under the pointer, on a wide diagram and on one that fits
//   tall <si>          the slider in the window anywhere inside a tall diagram, and the PNG items of a diagram too long
//                      for a canvas naming the part
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

const ADDED = /-\[hidden\]>|<space:|autonumber stop|hide footbox/;

async function frames(page, n) {
  await page.evaluate((n) => new Promise((done) => {
    let left = n;
    const tick = () => (--left <= 0 ? setTimeout(done, 0) : requestAnimationFrame(tick));
    requestAnimationFrame(tick);
  }), n);
}

async function openScenario(page, si) {
  await page.evaluate((si) => {
    const sc = document.querySelectorAll('details.scenario')[si];
    sc.setAttribute('data-probe', '1');
    sc.closest('details.feature').open = true; sc.open = true; sc.scrollIntoView();
    if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(sc);
  }, si);
  await page.waitForFunction(() => {
    const c = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
    if (!c) return false;
    const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
    const drawn = parts.length ? parts.every((p) => p.querySelector(':scope > svg')) : !!c.querySelector(':scope > svg');
    return drawn && !window._plantumlRendering && !c._lineUpPending && !c._zoomPending
      && !!c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
  }, null, { timeout: 300000, polling: 200 });
  await frames(page, 3);
}

// in the page: every part's level, its lifelines by two rules, and the box
const PAGE_HELPERS = `
  window.__acc = {
    box: (sel) => document.querySelector(sel),
    pics: (c) => { const p = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg')); return p.length ? p : Array.from(c.querySelectorAll(':scope > svg')); },
    level: (svg) => svg.getBoundingClientRect().width / svg.width.baseVal.value,
    // independent of the page's own rule: the dashed vertical lines, centred on the screen
    dashed: (svg) => {
      const at = {};
      Array.from(svg.querySelectorAll('line')).forEach((l) => {
        const x1 = +l.getAttribute('x1'), x2 = +l.getAttribute('x2');
        if (Math.abs(x1 - x2) > 0.01 || !/dasharray/.test(l.getAttribute('style') || '')) return;
        const k = x1.toFixed(2);
        if (!(k in at)) { const r = l.getBoundingClientRect(); at[k] = r.left + r.width / 2; }
      });
      return Object.values(at).sort((a, b) => a - b);
    },
    // the page's own rule (plantuml-browser-render-script.js lifelinesOf), on the screen
    rule: (svg) => {
      const xs = window._lifelinesOf ? window._lifelinesOf(svg) : [];
      const r = svg.getBoundingClientRect(), vb = svg.viewBox.baseVal;
      const k = vb && vb.width ? r.width / vb.width : 1;
      return xs.map((x) => r.left + (x - (vb ? vb.x : 0)) * k);
    },
    spread: (lists) => {
      if (lists.length < 2) return { counts: true, worst: 0 };
      const n = lists[0].length;
      if (!lists.every((l) => l.length === n)) return { counts: false, worst: NaN };
      let worst = 0;
      for (let i = 0; i < n; i++) { const v = lists.map((l) => l[i]); worst = Math.max(worst, Math.max(...v) - Math.min(...v)); }
      return { counts: true, worst };
    },
    wheel: (target, x, y, deltaY) => {
      const e = new WheelEvent('wheel', { ctrlKey: true, deltaY, clientX: x, clientY: y, bubbles: true, cancelable: true });
      target.dispatchEvent(e);
      return e.defaultPrevented;
    },
  };`;

(async () => {
  const [file, vw, vh, stage, siArg] = process.argv.slice(2);
  const si = +(siArg || 1);
  const browser = await launchInstalled(chromium, { headless: true });
  const out = (label, value) => console.log(label + ': ' + (typeof value === 'string' ? value : JSON.stringify(value)));
  try {
    const context = await browser.newContext({ viewport: { width: +vw, height: +vh } });
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    const page = await context.newPage();
    page.on('pageerror', (e) => console.log('pageerror', e.message));
    await page.goto('file:///' + path.resolve(file).split(path.sep).join('/'), { timeout: 600000 });
    await page.addScriptTag({ content: PAGE_HELPERS });
    out('viewport', vw + 'x' + vh);

    if (stage === 'sliders') {
      await page.evaluate(() => {
        document.querySelectorAll('details.feature, details.scenario').forEach((d) => { d.open = true; });
        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body);
      });
      // drawn when the count of pictures holds for 10 s with nothing rendering
      let last = -1, still = 0;
      for (let t = 0; t < 1800 && still < 10; t++) {
        const n = await page.evaluate(() => (window._plantumlRendering ? -2 : document.querySelectorAll('[data-diagram-type] svg').length));
        still = n === last && n >= 0 ? still + 1 : 0; last = n;
        await page.waitForTimeout(1000);
      }
      out('pictures drawn', String(last));
      const r = await page.evaluate(async () => {
        const A = window.__acc;
        const next = () => new Promise((d) => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(d, 30))));
        const boxes = Array.from(document.querySelectorAll('[data-diagram-type]'))
          .filter((b) => b.clientWidth > 0 && A.pics(b).length);
        const res = { boxes: boxes.length, noSlider: [], partControls: 0, mixedLevels: [], fits: [], fitScenarios: [], wide: 0, parts: 0,
          notLined: [], countsDiffer: [], ruleNotLined: [], stored: [], worstLined: 0 };
        for (let i = 0; i < boxes.length; i++) {
          const b = boxes[i];
          b.scrollIntoView({ block: 'center' });
          await next();
          const s = b.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
          const where = (b.closest('details.scenario') ? Array.prototype.indexOf.call(document.querySelectorAll('details.scenario'), b.closest('details.scenario')) : -1);
          if (!s) { res.noSlider.push(where); continue; }
          if (s.min === '50') { res.fits.push(i); res.fitScenarios.push(where); } else res.wide++;
          res.partControls += b.querySelectorAll(':scope > .puml-fragment > .diagram-zoom-controls').length;
          const pics = A.pics(b);
          if (pics.length > 1) {
            res.parts++;
            const lv = pics.map(A.level);
            if (Math.max(...lv) - Math.min(...lv) > 0.002) res.mixedLevels.push([where, lv.map((x) => +x.toFixed(4))]);
            const d = A.spread(pics.map(A.dashed));
            if (!d.counts) res.countsDiffer.push(where);
            else { res.worstLined = Math.max(res.worstLined, d.worst); if (d.worst > 0.5) res.notLined.push([where, +d.worst.toFixed(2)]); }
            const rr = A.spread(pics.map(A.rule));
            if (!rr.counts || rr.worst > 0.5) res.ruleNotLined.push([where, rr.counts ? +rr.worst.toFixed(2) : 'counts']);
          }
          const holders = [b].concat(Array.from(b.querySelectorAll(':scope > .puml-fragment')));
          holders.forEach((h) => {
            [h.getAttribute('data-plantuml'), h._fullSource, h._noteOriginalSource, h._drawnSource].forEach((t, k) => {
              if (typeof t === 'string' && /-\[hidden\]>|<space:|autonumber stop|hide footbox/.test(t)) res.stored.push([where, k]);
            });
          });
        }
        return res;
      });
      out('laid-out diagrams', String(r.boxes));
      out('without a slider (scenario indices)', r.noSlider.slice(0, 20));
      out('without a slider (count)', String(r.noSlider.length));
      out('wide / fits', r.wide + ' / ' + r.fits.length);
      out('scenarios of the first diagrams that fit', r.fitScenarios.slice(0, 8));
      out('sliders on a part', String(r.partControls));
      out('diagrams in parts', String(r.parts));
      out('parts at different levels', r.mixedLevels.slice(0, 10));
      out('dashed lifelines: counts differ', r.countsDiffer.slice(0, 20));
      out('dashed lifelines: not lined up > 0.5 px', r.notLined.slice(0, 20));
      out('dashed lifelines: worst spread px', r.worstLined.toFixed(3));
      out('page rule lifelines: not lined up', r.ruleNotLined.slice(0, 20));
      out('stored sources holding an added line [scenario, which]', r.stored.slice(0, 20));
      // the floor, on the first five diagrams that fit: 25 notches down end at 50 and go no further
      const floor = await page.evaluate(async (idx) => {
        const A = window.__acc;
        const next = () => new Promise((d) => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(d, 30))));
        const boxes = Array.from(document.querySelectorAll('[data-diagram-type]')).filter((b) => b.clientWidth > 0 && A.pics(b).length);
        const res = [];
        for (const i of idx.slice(0, 5)) {
          const b = boxes[i];
          b.scrollIntoView({ block: 'center' });
          await next();
          const pic = A.pics(b)[0], r = pic.getBoundingClientRect();
          let prevented = true;
          for (let n = 0; n < 25; n++) { prevented = A.wheel(pic, r.left + r.width / 2, Math.max(1, Math.min(innerHeight - 1, r.top + 20)), 100) && prevented; await next(); }
          const s = b.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
          res.push({ value: s.value, level: +A.level(A.pics(b)[0]).toFixed(3), prevented });
        }
        return res;
      }, r.fits);
      out('floor (value, level, every notch prevented)', floor);
    }

    if (stage === 'scenario') {
      await openScenario(page, si);
      const sel = 'details.scenario[data-probe] [data-diagram-type="plantuml"]';
      const before = await page.evaluate((sel) => { const c = window.__acc.box(sel); return window.__acc.pics(c).map((p) => +window.__acc.level(p).toFixed(4)); }, sel);
      out('parts and levels', before);
      for (let k = 0; k < before.length; k++) {
        const r = await page.evaluate(async ({ sel, k }) => {
          const A = window.__acc, c = A.box(sel), pics = A.pics(c);
          pics[k].scrollIntoView({ block: 'center' });
          await new Promise((d) => requestAnimationFrame(() => requestAnimationFrame(d)));
          const rr = pics[k].getBoundingClientRect();
          const was = pics.map(A.level);
          const prevented = A.wheel(pics[k], rr.left + rr.width / 2, Math.max(1, Math.min(innerHeight - 1, rr.top + rr.height / 2)), -100);
          for (let t = 0; t < 120 && c._zoomPending; t++) await new Promise((d) => setTimeout(d, 50));
          await new Promise((d) => requestAnimationFrame(() => requestAnimationFrame(d)));
          const now = A.pics(c).map(A.level);
          const back = A.wheel(pics[k], rr.left + rr.width / 2, Math.max(1, Math.min(innerHeight - 1, rr.top + rr.height / 2)), 100);
          for (let t = 0; t < 120 && c._zoomPending; t++) await new Promise((d) => setTimeout(d, 50));
          return { prevented, backPrevented: back, was: +was[0].toFixed(4), now: now.map((x) => +x.toFixed(4)) };
        }, { sel, k });
        out('wheel over part ' + (k + 1), r);
      }
      const ring = await page.evaluate(async (sel) => {
        const A = window.__acc, c = A.box(sel), pics = A.pics(c), last = pics[pics.length - 1];
        const r = last.getBoundingClientRect();
        last.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: r.left + 5, clientY: r.top + 5 }));
        await new Promise((d) => requestAnimationFrame(d));
        const cr = c.getBoundingClientRect();
        const encloses = pics.every((p) => { const q = p.parentElement.getBoundingClientRect(); return q.top >= cr.top - 0.5 && q.bottom <= cr.bottom + 0.5; });
        const res = { container: c.classList.contains('diagram-selected'), parts: c.querySelectorAll('.puml-fragment.diagram-selected').length, encloses };
        last.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: r.left + 5, clientY: r.top + 5 }));
        res.afterSecondClick = c.classList.contains('diagram-selected');
        return res;
      }, sel);
      out('a click on the last part', ring);
      const footbox = await page.evaluate((sel) => {
        const A = window.__acc, c = A.box(sel), pics = A.pics(c);
        // a participant drawn with its footbox has its name twice; count the first participant's name per part
        const first = (c.getAttribute('data-plantuml') || '').split('\n').map((l) => l.trim())
          .map((l) => /^(participant|actor|boundary|control|entity|database|collections|queue)\s+"?([^"]+?)"?(\s+as\s+\S+)?(\s|$)/.exec(l))
          .filter(Boolean)[0];
        const name = first ? first[2] : null;
        return { name, counts: name ? pics.map((p) => Array.from(p.querySelectorAll('text')).filter((t) => t.textContent.trim() === name).length) : [] };
      }, sel);
      out('the first participant named in each part (1 = no footbox)', footbox);
      // the SVG copy, over the second part
      const copy = async (label, startsWith) => {
        await page.evaluate(() => navigator.clipboard.writeText('nothing copied yet'));
        // scrolled first, and the menu opened frames later: the page closes its menu on a scroll
        await page.evaluate((sel) => { const pics = window.__acc.pics(window.__acc.box(sel)); pics[Math.min(1, pics.length - 1)].scrollIntoView({ block: 'center' }); }, sel);
        await frames(page, 3);
        await page.evaluate(({ sel, label }) => {
          document.querySelectorAll(".diagram-ctx-menu").forEach((m) => m.remove());
          const A = window.__acc, c = A.box(sel), pics = A.pics(c), p = pics[Math.min(1, pics.length - 1)];
          const r = p.getBoundingClientRect();
          p.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 }));
        }, { sel, label });
        await page.waitForFunction(() => !!document.querySelector(".diagram-ctx-menu"), null, { timeout: 10000, polling: 100 });
        const items = await page.evaluate(() => Array.from(document.querySelectorAll('.diagram-ctx-menu *')).filter((e) => e.children.length === 0).map((e) => e.textContent.trim()).filter(Boolean));
        // an item of a submenu is not shown until its submenu is hovered: clicked by script, as its listener takes it
        const clicked = await page.evaluate((labels) => {
          for (const one of labels) {
            const el = Array.from(document.querySelectorAll('.diagram-ctx-menu *')).find((e) => e.children.length === 0 && e.textContent.trim() === one);
            if (el) { el.click(); return one; }
          }
          return null;
        }, Array.isArray(label) ? label : [label]);
        if (!clicked) return { items, text: null };
        for (let t = 0; t < 100; t++) {
          const text = await page.evaluate(() => navigator.clipboard.readText().catch(() => ''));
          if (text.startsWith(startsWith)) return { items, text };
          await page.waitForTimeout(200);
        }
        return { items, text: null };
      };
      const svgCopy = await copy('Copy as SVG', '<svg');
      out('menu items over part 2', svgCopy.items);
      const svgFacts = await page.evaluate(({ sel, text }) => {
        if (!text) return { copied: false };
        const doc = new DOMParser().parseFromString(text, 'image/svg+xml');
        const ids = Array.from(doc.querySelectorAll('[id]')).map((e) => e.id);
        const c = window.__acc.box(sel), pics = window.__acc.pics(c);
        return {
          parses: !doc.querySelector('parsererror'),
          texts: doc.querySelectorAll('text').length,
          partsTexts: pics.reduce((n, p) => n + p.querySelectorAll('text').length, 0),
          idsTwice: ids.length - new Set(ids).size,
          twice: Array.from(new Set(ids.filter((x, i) => ids.indexOf(x) !== i))).slice(0, 8),
          withinAPart: pics.map((p) => { const a = Array.from(p.querySelectorAll('[id]')).map((e) => e.id); return a.length - new Set(a).size; }),
          plantumlSrc: (text.match(/plantuml-src/g) || []).length,
          height: doc.documentElement.getAttribute('height'),
          partsHeight: pics.reduce((n, p) => n + p.height.baseVal.value, 0),
        };
      }, { sel, text: svgCopy.text });
      out('the SVG copy', svgFacts);
      const src = await copy(['Copy full PlantUML source', 'Copy PlantUML source'], '@startuml');
      out('the copied full source holds an added line', String(src.text ? ADDED.test(src.text) : 'not copied'));
    }

    if (stage === 'point') {
      await openScenario(page, si);
      const sel = 'details.scenario[data-probe] [data-diagram-type="plantuml"]';
      const r = await page.evaluate(async (sel) => {
        const A = window.__acc, c = A.box(sel);
        const res = [];
        for (const dir of [-100, 100, 100, -100]) {
          const pics = A.pics(c);
          const p = pics.find((q) => { const rr = q.getBoundingClientRect(); return rr.top < innerHeight / 2 && rr.bottom > innerHeight / 2; }) || pics[0];
          if (!res.length) { const rr = p.getBoundingClientRect(); window.scrollBy(0, rr.top + rr.height / 2 - innerHeight / 2); await new Promise((d) => requestAnimationFrame(() => requestAnimationFrame(d))); }
          const rr = p.getBoundingClientRect();
          const x = rr.left + rr.width * 0.7 > innerWidth - 5 ? innerWidth / 2 : rr.left + rr.width * 0.7, y = innerHeight / 2;
          const fx = (x - rr.left) / rr.width, fy = (y - rr.top) / rr.height;
          const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
          const v0 = s.value;
          const prevented = A.wheel(p, x, y, dir);
          for (let t = 0; t < 120 && c._zoomPending; t++) await new Promise((d) => setTimeout(d, 50));
          await new Promise((d) => requestAnimationFrame(() => requestAnimationFrame(d)));
          const r2 = p.getBoundingClientRect();
          res.push({ dir, from: v0, to: s.value, wide: s.min !== '50', prevented, dx: +(r2.left + fx * r2.width - x).toFixed(2), dy: +(r2.top + fy * r2.height - y).toFixed(2) });
        }
        return res;
      }, sel);
      r.forEach((x) => out('notch', x));
    }

    if (stage === 'tall') {
      await openScenario(page, si);
      const sel = 'details.scenario[data-probe] [data-diagram-type="plantuml"]';
      const geo = await page.evaluate((sel) => { const r = window.__acc.box(sel).getBoundingClientRect(); return { top: r.top + scrollY, height: r.height }; }, sel);
      out('diagram height', String(Math.round(geo.height)));
      for (const depth of [0.1, 0.3, 0.5, 0.7, 0.9]) {
        await page.evaluate((y) => window.scrollTo(0, y), geo.top + depth * geo.height - (+vh) / 2);
        await frames(page, 2);
        await page.mouse.move((+vw) / 2, (+vh) / 2);
        await page.mouse.move((+vw) / 2 + 3, (+vh) / 2 + 3);
        await frames(page, 3);
        const s = await page.evaluate((sel) => {
          const c = window.__acc.box(sel), ctl = c.querySelector(':scope > .diagram-zoom-controls'), r = ctl.querySelector('.diagram-zoom-slider').getBoundingClientRect();
          return { top: Math.round(r.top), bottom: Math.round(r.bottom), opacity: getComputedStyle(ctl).opacity, inWindow: r.top >= 0 && r.bottom <= innerHeight };
        }, sel);
        out('slider at depth ' + depth, s);
      }
      await page.evaluate((sel) => { const pics = window.__acc.pics(window.__acc.box(sel)); pics[Math.min(1, pics.length - 1)].scrollIntoView({ block: 'center' }); }, sel);
      await frames(page, 3);
      await page.evaluate((sel) => {
        document.querySelectorAll(".diagram-ctx-menu").forEach((m) => m.remove());
        const A = window.__acc, c = A.box(sel), pics = A.pics(c), p = pics[Math.min(1, pics.length - 1)];
        const r = p.getBoundingClientRect();
        p.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 }));
      }, sel);
      await page.waitForFunction(() => !!document.querySelector(".diagram-ctx-menu"), null, { timeout: 10000, polling: 100 });
      const items = await page.evaluate(() => Array.from(document.querySelectorAll('.diagram-ctx-menu *')).filter((e) => e.children.length === 0).map((e) => e.textContent.trim()).filter((t) => /PNG|SVG/.test(t)));
      out('picture items over part 2', items);
    }
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
