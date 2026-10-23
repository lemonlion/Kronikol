'use strict';
// One scenario of a real report (index among details.scenario), every diagram box drawn: for each box, whether a
// Ctrl+wheel over it is taken by the page's zoom (not prevented = the browser zooms the page), the text sizes its
// picture is drawn with, and, for a box with a slider, how far one Ctrl+wheel step in moves the picture's point under
// the pointer, across (x) and down the page (y). usage: node probe-behaviour.js <report.html> <scenarioIndex> <vw> <vh>
const path = require('path');
// the bundled Chromium of the Playwright install the E2E project restores; launchInstalled as CLAUDE.md asks
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
  const [file, siArg, vw, vh] = process.argv.slice(2);
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
      const sc = document.querySelector('details.scenario[data-probe]');
      const boxes = Array.from(sc.querySelectorAll('[data-diagram-type="plantuml"]'));
      return boxes.length && boxes.every((c) => {
        const parts = c.querySelectorAll(':scope > .puml-fragment');
        return parts.length ? Array.from(parts).every((p) => p.querySelector(':scope > svg')) : c.querySelector(':scope > svg');
      });
    }, null, { timeout: 180000, polling: 200 });
    // each box scrolled into view so its slider is made (the observer gives one only near the screen)
    const n = await page.evaluate(() => {
      const sc = document.querySelector('details.scenario[data-probe]');
      const boxes = [];
      sc.querySelectorAll('[data-diagram-type="plantuml"]').forEach((c) => {
        const parts = c.querySelectorAll(':scope > .puml-fragment');
        (parts.length ? Array.from(parts) : [c]).forEach((b) => boxes.push(b));
      });
      boxes.forEach((b, i) => b.setAttribute('data-probe-box', String(i)));
      return boxes.length;
    });
    for (let i = 0; i < n; i++) {
      await page.evaluate((i) => document.querySelector(`[data-probe-box="${i}"]`).scrollIntoView({ block: 'center' }), i);
      await page.waitForTimeout(400);
    }
    for (let i = 0; i < n; i++) {
      await page.evaluate((i) => document.querySelector(`[data-probe-box="${i}"]`).scrollIntoView({ block: 'center' }), i);
      await page.waitForTimeout(300);
      const r = await page.evaluate((i) => {
        const b = document.querySelector(`[data-probe-box="${i}"]`);
        const svg = b.querySelector(':scope > svg');
        const slider = b.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
        const sizes = {};
        svg.querySelectorAll('text').forEach((t) => { const s = t.getAttribute('font-size'); if (s) sizes[s] = (sizes[s] || 0) + 1; });
        const scale = svg.getBoundingClientRect().width / svg.width.baseVal.value;
        // the point under the pointer: the middle of the box's visible part
        const br = b.getBoundingClientRect();
        const x = br.left + br.width / 2, y = Math.max(br.top, 0) + Math.min(br.bottom, innerHeight) / 2 - Math.max(br.top, 0) / 2;
        const sr0 = svg.getBoundingClientRect();
        const fx = (x - sr0.left) / sr0.width, fy = (y - sr0.top) / sr0.height;
        const ev = new WheelEvent('wheel', { bubbles: true, cancelable: true, ctrlKey: true, deltaY: -100, clientX: x, clientY: y });
        const notPrevented = svg.dispatchEvent(ev);
        const sr1 = svg.getBoundingClientRect();
        // where the picture point that was under the pointer is now, against the pointer
        const dx = sr1.left + fx * sr1.width - x, dy = sr1.top + fy * sr1.height - y;
        return { box: i, part: b.classList.contains('puml-fragment'), natW: svg.width.baseVal.value, natH: svg.height.baseVal.value,
          content: b.clientWidth, scaleBefore: +scale.toFixed(3), slider: slider ? { min: +slider.min, value: +slider.value } : null,
          pageZooms: notPrevented, fontSizes: sizes, afterStep: { scale: +(sr1.width / svg.width.baseVal.value).toFixed(3), dx: +dx.toFixed(1), dy: +dy.toFixed(1) } };
      }, i);
      console.log(JSON.stringify(r));
    }
  } finally {
    await browser.close();
  }
})();
