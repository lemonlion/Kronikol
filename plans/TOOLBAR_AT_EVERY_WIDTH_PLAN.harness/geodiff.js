// Which elements change geometry under an injected rule? Every element's rect and line count, with and without
// the CSS, every <details> open and every holder laid out; prints the first twenty that changed.
// Usage: INJECT='css' node geodiff.js <page.html> <w1,w2,...>
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const [, , file, widthsArg] = process.argv;
const INJECT = process.env.INJECT || '';
(async () => {
  const browser = await pw.chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 320, height: 900 } });
  await ctx.route(/^https?:/, r => r.abort());
  const page = await ctx.newPage();
  for (const w of widthsArg.split(',').map(Number)) {
    const runs = [];
    for (const css of ['', INJECT]) {
      await page.setViewportSize({ width: w, height: 900 });
      await page.goto('file:///' + path.resolve(file).replace(/\\/g, '/'), { waitUntil: 'domcontentloaded', timeout: 120000 });
      await page.waitForSelector('details.feature', { state: 'attached' });
      if (css) await page.addStyleTag({ content: css });
      runs.push(await page.evaluate(() => {
        document.querySelectorAll('details').forEach(d => d.open = true);
        document.querySelectorAll('.feature, .scenario').forEach(h => h.style.contentVisibility = 'visible');
        const f = document.querySelector('.filters'); if (f && getComputedStyle(f).display === 'none') f.style.display = 'flex';
        document.querySelectorAll('.scenario-diagram-controls').forEach(c => { if (getComputedStyle(c).display === 'none') c.style.display = 'flex'; });
        const tl = document.getElementById('scenario-timeline'); if (tl) tl.style.display = '';
        document.querySelectorAll('.search-help-panel').forEach(x => x.style.display = '');
        const range = document.createRange();
        const all = [...document.querySelectorAll('body *')];
        return all.map((e, i) => {
          const r = e.getBoundingClientRect();
          range.selectNodeContents(e);
          const lines = e.children.length === 0 ? range.getClientRects().length : 0;
          const name = e.localName + (e.id ? '#' + e.id : '') + [...e.classList].map(c => '.' + c).join('');
          const text = (e.textContent || '').trim().slice(0, 40);
          return [i, name, Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height), lines, text];
        });
      }));
    }
    const [a, b] = runs;
    const changed = [];
    for (let i = 0; i < Math.min(a.length, b.length); i++) {
      const [, name, l1, t1, w1, h1, n1, text] = a[i], [, , l2, t2, w2, h2, n2] = b[i];
      if (w1 !== w2 || h1 !== h2 || n1 !== n2) changed.push(`${name} "${text}" ${w1}x${h1}/${n1}L -> ${w2}x${h2}/${n2}L`);
    }
    console.log(`== ${path.basename(file)} at ${w}: ${changed.length} changed`);
    changed.slice(0, 20).forEach(c => console.log('  ' + c));
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
