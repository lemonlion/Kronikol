// node f1-oo.js : object-heavy JavaScript (allocations, polymorphic calls, string building), the kind TeaVM compiles
// PlantUML to, timed in each of Playwright's bundled engines (plans/HTML_COMPRESSION_PLAN.md F1). A build without the
// optimizing JIT tiers falls far behind on this while keeping up on a plain loop. Bundled browsers only.
const pw = require('playwright-core');
(async () => {
  for (const name of ['chromium', 'firefox', 'webkit']) {
    const browser = await pw[name].launch({ headless: true });
    try {
      const page = await browser.newPage();
      await page.setContent('<p>oo</p>');
      const ms = await page.evaluate(() => {
        class Shape { area() { return 0; } name() { return 'shape'; } }
        class Rect extends Shape { constructor(w, h) { super(); this.w = w; this.h = h; } area() { return this.w * this.h; } name() { return 'rect'; } }
        class Circle extends Shape { constructor(r) { super(); this.r = r; } area() { return 3.14159 * this.r * this.r; } name() { return 'circle'; } }
        class Tri extends Shape { constructor(b, h) { super(); this.b = b; this.h = h; } area() { return this.b * this.h / 2; } name() { return 'tri'; } }
        const t = performance.now();
        let total = 0, text = '';
        for (let round = 0; round < 40; round++) {
          const shapes = [];
          for (let i = 0; i < 20000; i++) shapes.push(i % 3 === 0 ? new Rect(i, 2) : i % 3 === 1 ? new Circle(i % 7) : new Tri(i, 3));
          const map = new Map();
          for (const s of shapes) { total += s.area(); const k = s.name(); map.set(k, (map.get(k) || 0) + 1); }
          text = Array.from(map.entries()).map(e => e[0] + '=' + e[1]).join(',');
        }
        window.__oo = [total, text];
        return performance.now() - t;
      });
      console.log(name, browser.version(), Math.round(ms) + ' ms');
    } finally { await browser.close(); }
  }
})().catch(e => { console.error(e); process.exitCode = 1; });
