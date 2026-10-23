'use strict';
// One scenario's diagram drawn in parts (Q2): where each part's lifelines stand, what a zoom step costs when every part
// is resized against one part, and whether a box's slider stays on screen once the page is scrolled into a tall box.
// usage: node probe-parts.js <report.html> <scenarioIndex> <vw> <vh> [reps]
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
  const [file, siArg, vw, vh, repsArg] = process.argv.slice(2);
  const reps = +(repsArg || 15);
  const browser = await launchInstalled(chromium, { headless: true });
  try {
    const page = await (await browser.newContext({ viewport: { width: +vw, height: +vh } })).newPage();
    page.on('pageerror', (e) => console.log('pageerror', e.message));
    await page.goto('file:///' + path.resolve(file).split(path.sep).join('/'), { timeout: 600000 });
    await page.evaluate((si) => {
      const sc = document.querySelectorAll('details.scenario')[si];
      sc.setAttribute('data-probe', '1');
      sc.closest('details.feature').open = true; sc.open = true; sc.scrollIntoView();
      if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(sc);
    }, +siArg);
    await page.waitForFunction(() => {
      const c = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
      const parts = c ? c.querySelectorAll(':scope > .puml-fragment') : [];
      return parts.length > 1 && Array.from(parts).every((p) => p.querySelector(':scope > svg .k-lifeline'));
    }, null, { timeout: 180000, polling: 200 });
    await page.waitForTimeout(1500);

    // 1. lifelines: each part's lifeline centres in the part's own units (the engine lays each part out on its own)
    const lifelines = await page.evaluate(() => {
      const c = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
      return Array.from(c.querySelectorAll(':scope > .puml-fragment')).map((p) => {
        const svg = p.querySelector(':scope > svg');
        const xs = Array.from(svg.querySelectorAll('.k-lifeline')).map((l) => { const b = l.getBBox(); return Math.round(b.x + b.width / 2); });
        return { natW: svg.width.baseVal.value, xs: Array.from(new Set(xs)).sort((a, b) => a - b) };
      });
    });
    lifelines.forEach((p, i) => console.log(`part ${i + 1}: natural width ${p.natW}, ${p.xs.length} lifelines at ${p.xs.join(' ')}`));

    // 2. a zoom step's layout: one part's picture resized and laid out, against every part's
    const cost = await page.evaluate((reps) => {
      const c = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
      const svgs = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'));
      const nat = svgs.map((s) => s.width.baseVal.value);
      function step(which, scale) {
        const t0 = performance.now();
        which.forEach((i) => { svgs[i].style.maxWidth = 'none'; svgs[i].style.width = (nat[i] * scale) + 'px'; });
        void c.getBoundingClientRect().height; svgs.forEach((s) => s.getBoundingClientRect());
        return performance.now() - t0;
      }
      const one = [], all = [], everyIndex = svgs.map((_, i) => i);
      for (let r = 0; r < reps; r++) {
        one.push(step([0], 0.80 + (r % 2) * 0.02));
        all.push(step(everyIndex, 0.80 + (r % 2) * 0.02));
      }
      svgs.forEach((s) => { s.style.maxWidth = ''; s.style.width = ''; });
      const med = (a) => a.slice().sort((x, y) => x - y)[Math.floor(a.length / 2)].toFixed(1);
      const texts = svgs.map((s) => s.querySelectorAll('text').length);
      return { parts: svgs.length, texts, oneMs: med(one), allMs: med(all) };
    }, reps);
    console.log(`zoom step layout: ${cost.parts} parts (${cost.texts.join(', ')} <text>): one part ${cost.oneMs} ms, every part ${cost.allMs} ms (median of ${reps})`);

    // 3. the slider once the page is scrolled 1,500 px into the first part (sticky: top 2em)
    await page.evaluate(() => {
      const c = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
      c.querySelector(':scope > .puml-fragment').scrollIntoView({ block: 'center' });
    });
    await page.waitForTimeout(800);
    const sticky = await page.evaluate(() => {
      const c = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
      const part = c.querySelector(':scope > .puml-fragment');
      window.scrollTo(0, window.scrollY + part.getBoundingClientRect().top + 1500);
      const controls = part.querySelector(':scope > .diagram-zoom-controls');
      const pr = part.getBoundingClientRect();
      return { partTop: Math.round(pr.top), partBottom: Math.round(pr.bottom), controlsTop: controls ? Math.round(controls.getBoundingClientRect().top) : null,
        containerOverflowY: getComputedStyle(c).overflowY, partPosition: getComputedStyle(controls || part).position };
    });
    console.log(`scrolled into part 1: part spans ${sticky.partTop} to ${sticky.partBottom} px of the window; its slider's top at ${sticky.controlsTop} px (${sticky.partPosition}); the container's overflow-y computes to ${sticky.containerOverflowY}`);
  } finally {
    await browser.close();
  }
})();
