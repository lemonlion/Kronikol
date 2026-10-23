'use strict';
// What a reply's note beside the caller (`note right` -> `note left` after a reply) does to every diagram, not only to
// those drawn in parts. Draws every diagram of a report (as probe-widths.js does), then sends the source of each laid-out
// sequence picture (a whole diagram, or one part) to the engine as it is and with its notes moved, and compares the
// two widths, and the level each would be drawn at in its box (its fit, at most 100%).
// usage: node probe-notes-left.js <report.html> [width] [height]
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
  const [file, vw = '1920', vh = '1000'] = process.argv.slice(2);
  const browser = await launchInstalled(chromium, { headless: true });
  try {
    const page = await (await browser.newContext({ viewport: { width: +vw, height: +vh } })).newPage();
    page.on('pageerror', (e) => console.log('pageerror', e.message));
    await page.goto('file:///' + path.resolve(file).split(path.sep).join('/'), { timeout: 600000 });
    await page.evaluate(() => {
      document.querySelectorAll('details.feature, details.scenario').forEach((d) => { d.open = true; });
      window._renderDiagramsInContainer(document.body);
    });
    let last = -1, since = Date.now();
    for (;;) {
      const s = await page.evaluate(() => {
        const boxes = Array.from(document.querySelectorAll('details.scenario [data-diagram-type="plantuml"]'));
        let drawn = 0;
        boxes.forEach((c) => {
          const parts = c.querySelectorAll(':scope > .puml-fragment');
          if (parts.length ? Array.from(parts).every((p) => p.querySelector(':scope > svg')) : c.querySelector(':scope > svg')) drawn++;
        });
        return { total: boxes.length, drawn, busy: !!window._plantumlRendering };
      });
      if (s.drawn !== last) { last = s.drawn; since = Date.now(); }
      if ((s.drawn === s.total && !s.busy) || Date.now() - since > 60000) { console.log(`drawn ${s.drawn} of ${s.total}`); break; }
      await page.waitForTimeout(1000);
    }
    await page.waitForTimeout(3000);

    const out = await page.evaluate(async () => {
      let seq = 0;
      function render(source) {
        return new Promise((resolve) => {
          const div = document.createElement('div');
          div.id = 'probe-notes-left-' + (seq++);
          div.style.cssText = 'position:absolute;left:-300000px;top:0;visibility:hidden';
          document.body.appendChild(div);
          const mo = new MutationObserver(() => done());
          function done() {
            const svg = div.querySelector('svg');
            if (!svg && !div.textContent.trim()) return;
            mo.disconnect();
            const r = svg ? { w: svg.width.baseVal.value, h: svg.height.baseVal.value } : { error: div.textContent.trim().slice(0, 120) };
            div.remove();
            resolve(r);
          }
          mo.observe(div, { childList: true, subtree: true, characterData: true });
          window.plantuml.render(source.split('\n'), div.id);
          done();
        });
      }
      const bust = (src, tag) => src.replace(/^@startuml.*$/m, (m) => m + "\n' probe " + tag + ' ' + Math.random().toString(36).slice(2));
      const left = (s) => s.replace(/^(\s*note(?:<<[^>]*>>)?\s+)right(\s*)$/gm, '$1left$2');
      // every laid-out sequence picture: a whole diagram's container, or a part; its box's content width
      const boxes = [];
      const scenarios = Array.from(document.querySelectorAll('details.scenario'));
      document.querySelectorAll('details.scenario [data-diagram-type="plantuml"]').forEach((c) => {
        const si = scenarios.indexOf(c.closest('details.scenario'));
        const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
        const own = parts.length ? parts : [c];
        own.forEach((b) => {
          const svg = b.querySelector(':scope > svg');
          if (!svg || !b.clientWidth) return;
          const cs = getComputedStyle(b);
          const src = b === c ? (c._fullSource || c.getAttribute('data-plantuml')) : b.getAttribute('data-plantuml');
          if (!src || !/^\s*note(?:<<[^>]*>>)?\s+right\s*$/m.test(src)) { boxes.push({ inParts: b !== c, skip: true }); return; }
          boxes.push({ si, inParts: b !== c, src, content: b.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight) });
        });
      });
      const rows = [];
      for (let i = 0; i < boxes.length; i += 4) {
        await Promise.all(boxes.slice(i, i + 4).map(async (b) => {
          if (b.skip) { rows.push({ inParts: b.inParts, skip: true }); return; }
          const [a, l] = await Promise.all([render(bust(b.src, 'as')), render(bust(left(b.src), 'left'))]);
          if (a.error || l.error) { rows.push({ inParts: b.inParts, error: true }); return; }
          rows.push({ si: b.si, inParts: b.inParts, w: a.w, wl: l.w, h: a.h, hl: l.h, content: b.content });
        }));
      }
      return rows;
    });
    const ok = out.filter((r) => !r.skip && !r.error);
    const pct = (a, p) => a[Math.min(a.length - 1, Math.floor(p * a.length))];
    const pc = (x) => (x >= 0 ? '+' : '') + (100 * x).toFixed(1) + '%';
    const fit = (w, c) => Math.min(1, c / w);
    console.log(`pictures: ${out.length}, with no reply note on the right ${out.filter((r) => r.skip).length}, errors ${out.filter((r) => r.error).length}, measured ${ok.length}`);
    for (const [name, rs] of [['all', ok], ['whole diagrams', ok.filter((r) => !r.inParts)], ['parts', ok.filter((r) => r.inParts)]]) {
      if (!rs.length) continue;
      const dw = rs.map((r) => r.wl / r.w - 1).sort((x, y) => x - y);
      const dh = rs.map((r) => r.hl / r.h - 1).sort((x, y) => x - y);
      const lv = rs.map((r) => fit(r.wl, r.content) / fit(r.w, r.content) - 1).sort((x, y) => x - y);
      console.log(`${name} (${rs.length}): width ${pc(pct(dw, 0))} p10 ${pc(pct(dw, 0.1))} median ${pc(pct(dw, 0.5))} p90 ${pc(pct(dw, 0.9))} max ${pc(dw[dw.length - 1])}; ` +
        `narrower ${dw.filter((x) => x < -0.005).length}, wider ${dw.filter((x) => x > 0.005).length}; height median ${pc(pct(dh, 0.5))} max ${pc(dh[dh.length - 1])}; ` +
        `level in its box: median ${pc(pct(lv, 0.5))} p90 ${pc(pct(lv, 0.9))} max ${pc(lv[lv.length - 1])}, lower ${lv.filter((x) => x < -0.005).length}; ` +
        `fitting at 100%: ${rs.filter((r) => r.w <= r.content).length} -> ${rs.filter((r) => r.wl <= r.content).length}`);
    }
    const fitOf = (w, c) => Math.min(1, c / w);
    const lower = ok.map((r) => ({ r, d: fitOf(r.wl, r.content) / fitOf(r.w, r.content) - 1 })).filter((x) => x.d < -0.005).sort((a, b) => a.d - b.d);
    console.log(`drawn smaller: ${lower.length}; by more than 5%: ${lower.filter((x) => x.d < -0.05).length}; by more than 10%: ${lower.filter((x) => x.d < -0.1).length}`);
    lower.slice(0, 8).forEach((x) => console.log(`  scenario ${x.r.si}${x.r.inParts ? ' (a part)' : ''}: ${Math.round(x.r.w)} -> ${Math.round(x.r.wl)} px in a ${Math.round(x.r.content)} px box, level ${(100 * fitOf(x.r.w, x.r.content)).toFixed(1)}% -> ${(100 * fitOf(x.r.wl, x.r.content)).toFixed(1)}%`));
  } finally {
    await browser.close();
  }
})();
