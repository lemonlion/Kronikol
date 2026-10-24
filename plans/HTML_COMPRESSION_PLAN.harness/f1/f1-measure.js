// node f1-measure.js : canvas 2D measureText, as the PlantUML engine calls it for every label, timed in each of
// Playwright's bundled engines (plans/HTML_COMPRESSION_PLAN.md F1). Bundled browsers only.
const pw = require('playwright-core');
(async () => {
  for (const name of ['chromium', 'firefox', 'webkit']) {
    const browser = await pw[name].launch({ headless: true });
    try {
      const page = await browser.newPage();
      await page.setContent('<canvas id="c"></canvas>');
      const r = await page.evaluate(() => {
        const ctx = document.getElementById('c').getContext('2d');
        const fonts = ['14px sans-serif', '13px monospace', 'bold 14px sans-serif', 'italic 12px serif'];
        let w = 0;
        const t = performance.now();
        for (let i = 0; i < 5000; i++) {
          ctx.font = fonts[i % 4];
          w += ctx.measureText('GET: /api/orders/' + i + ' returned 200 OK').width;
        }
        const font = performance.now() - t;
        const t2 = performance.now();
        ctx.font = '14px sans-serif';
        for (let i = 0; i < 5000; i++) w += ctx.measureText('GET: /api/orders/' + i + ' returned 200 OK').width;
        return { withFontChanges: Math.round(font), sameFont: Math.round(performance.now() - t2), w: Math.round(w) };
      });
      console.log(name, browser.version(), JSON.stringify(r));
    } finally { await browser.close(); }
  }
})().catch(e => { console.error(e); process.exitCode = 1; });
