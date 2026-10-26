'use strict';
// I5: where the zoom slider's thumb is PAINTED, against the pinned bar's bottom: a 2x screenshot of the slider's
// neighbourhood is scanned in a canvas for pixels of the slider's accent colour; the topmost such row is the thumb's top
// (the thumb is taller than the track). Also gives the thumb's half-height, which is what the slider's line has to clear.
// usage: node probe/i5-thumb.js <main.html> <outDir>
const path = require('path');
const pw = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
const { frames, openScenario, write } = require('./common');

(async () => {
  const [file, outDir] = process.argv.slice(2);
  const browser = await launchInstalled(pw.chromium, { headless: true });
  try {
    const page = await (await browser.newContext({ viewport: { width: 1600, height: 900 }, deviceScaleFactor: 2 })).newPage();
    await page.goto('file:///' + path.resolve(file).replace(/\\/g, '/'));
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
    await openScenario(page, 'Bake a large breakfast order');
    let boxTop = 0;
    for (let k = 0; k < 20 && boxTop > -1100; k++) {
      await page.evaluate(() => { const b = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]'); window.scrollTo(0, b.getBoundingClientRect().top + window.scrollY + 1200); });
      await frames(page, 20);
      boxTop = await page.evaluate(() => document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]').getBoundingClientRect().top);
    }
    const c = await page.evaluate(() => { const r = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]').getBoundingClientRect(); return { x: r.left + 300, y: 500 }; });
    await page.mouse.move(c.x, c.y);
    await frames(page, 6);
    // the slider fully shown, so its colour is its own (the box's hover gives 0.4)
    await page.evaluate(() => { const s = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"] > .diagram-zoom-controls'); s.style.opacity = '1'; });
    await frames(page, 4);
    const m = await page.evaluate(() => {
      const input = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"] .diagram-zoom-slider');
      const r = input.getBoundingClientRect(), bar = document.querySelector('.toolbar-row.report-nav').getBoundingClientRect();
      return { left: r.left, top: r.top, width: r.width, height: r.height, barBottom: bar.bottom, accent: getComputedStyle(input).accentColor };
    });
    const clip = { x: m.left - 6, y: m.barBottom - 12, width: m.width + 12, height: 40 };
    const png = await page.screenshot({ clip });
    const scan = await page.evaluate(async ([b64, accent, clipY, dpr]) => {
      const img = new Image(); img.src = 'data:image/png;base64,' + b64; await img.decode();
      const cv = document.createElement('canvas'); cv.width = img.width; cv.height = img.height;
      const g = cv.getContext('2d'); g.drawImage(img, 0, 0);
      const d = g.getImageData(0, 0, cv.width, cv.height).data;
      const probe = document.createElement('div'); probe.style.color = accent; document.body.appendChild(probe);
      const rgb = getComputedStyle(probe).color.match(/\d+/g).slice(0, 3).map(Number); probe.remove();
      let top = -1, bottom = -1;
      for (let y = 0; y < cv.height; y++) {
        let n = 0;
        for (let x = 0; x < cv.width; x++) { const i = (y * cv.width + x) * 4; if (Math.abs(d[i] - rgb[0]) + Math.abs(d[i + 1] - rgb[1]) + Math.abs(d[i + 2] - rgb[2]) < 60) n++; }
        if (n >= 2) { if (top < 0) top = y; bottom = y; }
      }
      return { rgb, thumbTop: clipY + top / dpr, thumbBottom: clipY + (bottom + 1) / dpr };
    }, [png.toString('base64'), m.accent, clip.y, 2]);
    const line = m.top + m.height / 2;
    const out = { boxTop: Math.round(boxTop), barBottom: m.barBottom, inputTop: +m.top.toFixed(2), line: +line.toFixed(2), ...scan,
      thumbClearOfBar: +(scan.thumbTop - m.barBottom).toFixed(2), halfThumb: +(line - scan.thumbTop).toFixed(2) };
    require('fs').writeFileSync(path.join(outDir, 'i5-thumb.png'), png);
    write(path.join(outDir, 'i5-thumb.json'), out);
    console.log(JSON.stringify(out, null, 2));
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
