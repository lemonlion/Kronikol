// node f1-jit.js : the same pure-JavaScript loop in each of Playwright's bundled engines, to see whether one of them runs
// without a JIT (an interpreter is tens of times slower on such a loop). Bundled browsers only.
const pw = require('playwright-core');
(async () => {
  for (const name of ['chromium', 'firefox', 'webkit']) {
    const browser = await pw[name].launch({ headless: true });
    try {
      const page = await browser.newPage();
      await page.setContent('<p>loop</p>');
      const ms = await page.evaluate(() => {
        const t = performance.now();
        let h = 2166136261 >>> 0;
        for (let i = 0; i < 20000000; i++) { h ^= i & 255; h = Math.imul(h, 16777619) >>> 0; }
        window.__h = h;
        return performance.now() - t;
      });
      console.log(name, browser.version(), Math.round(ms) + ' ms');
    } finally { await browser.close(); }
  }
})().catch(e => { console.error(e); process.exitCode = 1; });
