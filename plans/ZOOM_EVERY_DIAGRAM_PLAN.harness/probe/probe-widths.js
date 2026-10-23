'use strict';
// Every drawn diagram box of a report (a whole diagram's container, or each part of one drawn in parts): its picture's
// natural width and height, the box's width with and without its padding, today's fit level and whether today's rule
// gives it a slider. usage: node probe-widths.js <report.html> <viewportWidth> <viewportHeight> <out.json>
const path = require('path');
const fs = require('fs');
// the bundled Chromium of the Playwright install the E2E project restores; launchInstalled as CLAUDE.md asks
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
  const [file, vw, vh, out] = process.argv.slice(2);
  const browser = await launchInstalled(chromium, { headless: true });
  try {
    const page = await (await browser.newContext({ viewport: { width: +vw, height: +vh } })).newPage();
    page.on('pageerror', (e) => console.log('pageerror', e.message));
    const t0 = Date.now();
    await page.goto('file:///' + path.resolve(file).split(path.sep).join('/'), { timeout: 600000 });
    console.log('loaded', (Date.now() - t0) / 1000, 's');
    await page.evaluate(() => {
      document.querySelectorAll('details.feature, details.scenario').forEach((d) => { d.open = true; });
      if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body);
    });
    // drawn = every sequence/activity box holds its picture (whole) or every part holds one; stable for 5 s
    let last = -1, stableSince = Date.now();
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
      if (s.drawn !== last) { last = s.drawn; stableSince = Date.now(); console.log('drawn', s.drawn, '/', s.total, (Date.now() - t0) / 1000, 's'); }
      if ((s.drawn === s.total && !s.busy) || Date.now() - stableSince > 60000) break;
      await page.waitForTimeout(1000);
    }
    await page.waitForTimeout(3000);
    const rows = await page.evaluate(() => {
      const scenarios = Array.from(document.querySelectorAll('details.scenario'));
      const res = [];
      scenarios.forEach((sc, si) => {
        const boxes = Array.from(sc.querySelectorAll('[data-diagram-type="plantuml"]'))
          .filter((c) => c.closest('details.scenario') === sc);
        boxes.forEach((c, ci) => {
          const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
          (parts.length ? parts : [c]).forEach((b, pi) => {
            const svg = b.querySelector(':scope > svg');
            if (!svg) return;
            const cs = getComputedStyle(b);
            const pad = (parseFloat(cs.paddingLeft) || 0) + (parseFloat(cs.paddingRight) || 0);
            const w = svg.width && svg.width.baseVal, h = svg.height && svg.height.baseVal;
            res.push({
              si, ci, pi, parts: parts.length,
              kind: c.classList.contains('iflow-diagram') ? 'activity' : (c.hasAttribute('data-k-sequence') || b.hasAttribute('data-k-sequence') ? 'sequence' : 'other'),
              natW: w ? w.value : null, natH: h ? h.value : null, wUnit: w ? w.unitType : null,
              client: b.clientWidth, content: b.clientWidth - pad,
              drawnW: svg.getBoundingClientRect().width, drawnH: svg.getBoundingClientRect().height,
              slider: !!b.querySelector(':scope > .diagram-zoom-controls'),
            });
          });
        });
      });
      return { rows: res, scenarios: scenarios.length, vw: innerWidth, vh: innerHeight };
    });
    fs.writeFileSync(out, JSON.stringify(rows));
    console.log('rows', rows.rows.length, 'scenarios', rows.scenarios, 'written', out, (Date.now() - t0) / 1000, 's');
  } finally {
    await browser.close();
  }
})();
