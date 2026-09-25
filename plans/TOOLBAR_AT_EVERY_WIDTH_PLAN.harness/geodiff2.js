// Two builds of the same page (same markup, different stylesheet): which elements change size or line count?
// Every <details> open, every holder laid out, the phone-hidden filters and toolbars, the search help and the
// timeline shown. Usage: node geodiff2.js <dirA> <dirB> <page,...> <from-to-step> [scrollbars=0|1] [TS=1 env]
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const [, , dirA, dirB, pagesArg, widthsArg, sb] = process.argv;
const [from, to, step] = widthsArg.split('-').map(Number);
const widths = []; for (let w = from; w <= to; w += step) widths.push(w);
if (!widths.includes(769)) widths.push(769); if (!widths.includes(1161)) widths.push(1161);
widths.sort((a, b) => a - b);
const TS = process.env.TS === '1' ? '*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}' : '';
(async () => {
  const browser = await pw.chromium.launch(sb === '1' ? { ignoreDefaultArgs: ['--hide-scrollbars'] } : {});
  const ctx = await browser.newContext({ viewport: { width: 320, height: 900 } });
  await ctx.route(/^https?:/, r => r.abort());
  const page = await ctx.newPage();
  const measure = async (file, w) => {
    await page.setViewportSize({ width: w, height: 900 });
    await page.goto('file:///' + path.resolve(file).replace(/\\/g, '/'), { waitUntil: 'domcontentloaded', timeout: 120000 });
    await page.waitForSelector('details.feature', { state: 'attached' });
    if (TS) await page.addStyleTag({ content: TS });
    return page.evaluate(() => {
      document.querySelectorAll('details').forEach(d => d.open = true);
      document.querySelectorAll('.feature, .scenario').forEach(h => h.style.contentVisibility = 'visible');
      const f = document.querySelector('.filters'); if (f && getComputedStyle(f).display === 'none') f.style.display = 'flex';
      document.querySelectorAll('.scenario-diagram-controls').forEach(c => { if (getComputedStyle(c).display === 'none') c.style.display = 'flex'; });
      const tl = document.getElementById('scenario-timeline'); if (tl) tl.style.display = '';
      document.querySelectorAll('.search-help-panel').forEach(x => x.style.display = '');
      const range = document.createRange();
      return [...document.querySelectorAll('body *')].map(e => {
        const r = e.getBoundingClientRect();
        range.selectNodeContents(e);
        const lines = new Set([...range.getClientRects()].filter(x => x.width > 0).map(x => Math.round(x.top))).size;
        return [e.localName + [...e.classList].map(c => '.' + c).join(''), Math.round(r.width), Math.round(r.height), lines, (e.textContent || '').trim().slice(0, 30)];
      });
    });
  };
  for (const p of pagesArg.split(',')) {
    const diffs = [];
    for (const w of widths) {
      const a = await measure(path.join(dirA, p), w), b = await measure(path.join(dirB, p), w);
      if (a.length !== b.length) { diffs.push(`${w}: element count ${a.length} vs ${b.length}`); continue; }
      for (let i = 0; i < a.length; i++) {
        const [n, w1, h1, l1, t] = a[i], [, w2, h2, l2] = b[i];
        if (w1 !== w2 || h1 !== h2 || l1 !== l2) diffs.push(`${w}: ${n} "${t}" ${w1}x${h1}/${l1}L -> ${w2}x${h2}/${l2}L`);
      }
    }
    console.log(`== ${p}${TS ? ' (text spacing)' : ''}${sb === '1' ? ' (scrollbar)' : ''}: ${diffs.length} differences at ${widths.length} widths`);
    diffs.slice(0, 25).forEach(d => console.log('  ' + d));
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
