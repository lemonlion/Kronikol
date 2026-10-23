'use strict';
// Does drawing a long diagram in parts save time? Draws every diagram of a report (as probe-widths.js does), then for
// each diagram drawn in parts sends the engine (window.plantuml.render, the page's workers) its parts together, as the
// page's queue does, and then its whole source as one picture; a comment line makes each drawing miss the engine's
// cache. Wall-clock from the first send to the last picture, the sum of the parts' own times, the whole's time, height
// and text count, and whether the whole was drawn (the engine draws at most 98,304 px, `maxSvgSize`).
// usage: node probe-split.js <report.html> [width] [height] [rounds]
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
  const [file, vw = '1920', vh = '1000', roundsArg = '2'] = process.argv.slice(2);
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
    const out = await page.evaluate(async (rounds) => {
      let seq = 0;
      function render(source) {
        return new Promise((resolve) => {
          const div = document.createElement('div');
          div.id = 'probe-split-' + (seq++);
          div.style.cssText = 'position:absolute;left:-300000px;top:0;visibility:hidden';
          document.body.appendChild(div);
          const t0 = performance.now();
          const mo = new MutationObserver(() => done());
          function done() {
            const svg = div.querySelector('svg');
            if (!svg && !div.textContent.trim()) return;
            mo.disconnect();
            const r = svg ? { ms: performance.now() - t0, h: svg.height.baseVal.value, texts: svg.querySelectorAll('text').length }
              : { ms: performance.now() - t0, error: div.textContent.trim().slice(0, 100) };
            div.remove();
            resolve(r);
          }
          mo.observe(div, { childList: true, subtree: true, characterData: true });
          window.plantuml.render(source.split('\n'), div.id);
          done();
        });
      }
      const bust = (src, tag) => src.replace(/^@startuml.*$/m, (m) => m + "\n' probe " + tag + ' ' + Math.random().toString(36).slice(2));
      const scenarios = Array.from(document.querySelectorAll('details.scenario'));
      const rows = [];
      for (const c of Array.from(document.querySelectorAll('details.scenario [data-diagram-type="plantuml"]'))) {
        const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
        if (parts.length < 2) continue;
        const sources = parts.map((p) => p.getAttribute('data-plantuml'));
        const whole = c._fullSource || c.getAttribute('data-plantuml');
        const row = { si: scenarios.indexOf(c.closest('details.scenario')), parts: parts.length,
          partsH: Math.round(parts.reduce((a, p) => a + (p.querySelector('svg') ? p.querySelector('svg').height.baseVal.value : 0), 0)),
          partWall: [], partSum: [], wholeMs: [] };
        for (let r = 0; r < rounds; r++) {
          const t0 = performance.now();
          const rs = await Promise.all(sources.map((s) => render(bust(s, 'part'))));
          row.partWall.push(performance.now() - t0); row.partSum.push(rs.reduce((a, x) => a + x.ms, 0));
          if (whole) {
            const w = await render(bust(whole, 'whole'));
            row.wholeMs.push(w.ms); row.wholeH = Math.round(w.h || 0); row.wholeTexts = w.texts; row.wholeError = w.error || null;
          }
        }
        rows.push(row);
      }
      return { rows, workers: (window.__kronikolRender || {}).workers, maxParallel: window.plantuml.maxParallel };
    }, +roundsArg);
    const med = (a) => (a.length ? Math.round(a.slice().sort((x, y) => x - y)[Math.floor(a.length / 2)]) : '-');
    console.log(`workers ${out.workers}, at most ${out.maxParallel} in flight; times are the median of the rounds`);
    console.log('scenario parts | parts: wall sum | whole: ms height texts');
    out.rows.forEach((r) => console.log(`${r.si} ${r.parts} | ${med(r.partWall)} ${med(r.partSum)} | ${med(r.wholeMs)} ${r.wholeH}${r.wholeError ? ' ERROR ' + r.wholeError : ''} (parts ${r.partsH} px) ${r.wholeTexts || ''}`));
    const tot = (k) => out.rows.reduce((a, r) => a + (typeof med(r[k]) === 'number' ? med(r[k]) : 0), 0);
    console.log(`totals over ${out.rows.length} diagrams: parts wall ${tot('partWall')} ms (sum ${tot('partSum')}), whole ${tot('wholeMs')} ms`);
  } finally {
    await browser.close();
  }
})();
