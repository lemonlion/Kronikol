// Does a candidate rule change how a page PAINTS where nothing overflowed? Full-page screenshots with and
// without the injected CSS, every <details> open and the phone-hidden parts shown, compared pixel for pixel.
// Usage: INJECT='css' node shotdiff.js <page.html,...> <w1,w2,...> [engine]
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const [, , filesArg, widthsArg, engine] = process.argv;
const INJECT = process.env.INJECT || '';
(async () => {
  const browser = await pw[engine || 'chromium'].launch();
  for (const file of filesArg.split(',')) {
    const ctx = await browser.newContext({ viewport: { width: 320, height: 900 } });
    await ctx.route(/^https?:/, r => r.abort());
    const page = await ctx.newPage();
    const out = [];
    for (const w of widthsArg.split(',').map(Number)) {
      const shots = [];
      for (const css of ['', INJECT]) {
        await page.setViewportSize({ width: w, height: 900 });
        await page.goto('file:///' + path.resolve(file).replace(/\\/g, '/'), { waitUntil: 'domcontentloaded', timeout: 120000 });
        await page.waitForSelector('details.feature', { state: 'attached' });
        if (css) await page.addStyleTag({ content: css });
        await page.evaluate(() => {
          document.querySelectorAll('details').forEach(d => d.open = true);
          document.querySelectorAll('.feature, .scenario').forEach(h => h.style.contentVisibility = 'visible');
          const f = document.querySelector('.filters'); if (f && getComputedStyle(f).display === 'none') f.style.display = 'flex';
          document.querySelectorAll('.scenario-diagram-controls').forEach(c => { if (getComputedStyle(c).display === 'none') c.style.display = 'flex'; });
          const tl = document.getElementById('scenario-timeline'); if (tl) tl.style.display = '';
          document.querySelectorAll('.search-help-panel').forEach(x => x.style.display = '');
        });
        await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
        shots.push(await page.screenshot({ fullPage: true, animations: 'disabled', caret: 'hide' }));
      }
      out.push(`${w}:${shots[0].equals(shots[1]) ? 'same' : 'DIFFERS'}`);
    }
    console.log(`${path.basename(file)} [${engine || 'chromium'}]: ${out.join(' ')}`);
    await ctx.close();
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
