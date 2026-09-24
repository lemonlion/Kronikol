// Does a real browser land between `max-width: 768px` and `min-width: 769px`? A window whose device
// width divided by the device scale factor is fractional (961 px at 125 %, 1153 px at 150 %) gives a
// CSS viewport of 768.8 or 768.67 px, which neither query matches. Usage: node fractional.js [dir] [page]
// Chromium: --force-device-scale-factor plus --window-size, viewport null (the window decides).
// Firefox: layout.css.devPixelsPerPx plus a window size, viewport null.
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const DIR = process.argv[2] || 'out-head-patched';
const PAGE = process.argv[3] || 'TestRunReport.html';
const url = 'file:///' + path.join(__dirname, DIR, PAGE).split(path.sep).join('/');

const PROBE = `() => {
  const mq = q => matchMedia(q).matches;
  const hr = document.querySelector('.header-row');
  const box = document.querySelector('.filtering-box');
  return {
    innerWidth, clientWidth: document.documentElement.clientWidth, vv: visualViewport ? +visualViewport.width.toFixed(3) : null, dpr: devicePixelRatio,
    exactWidth: +document.documentElement.getBoundingClientRect().width.toFixed(3),
    phone: mq('(max-width: 768px)'), band: mq('(min-width: 769px) and (max-width: 1100px)'), rowOrBand769: mq('(min-width: 769px)'),
    gapAware: mq('(min-width: 768.02px)'),
    header: hr ? getComputedStyle(hr).flexDirection + '/' + getComputedStyle(hr).flexWrap : null,
    boxWidth: box ? +box.getBoundingClientRect().width.toFixed(1) : null,
    overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  };
}`;

(async () => {
  const cases = [[1.25, 960], [1.25, 961], [1.25, 962], [1.5, 1152], [1.5, 1153], [1.1, 845], [1.1, 846]];
  for (const [dsf, win] of cases) {
    const b = await pw.chromium.launch({ args: [`--force-device-scale-factor=${dsf}`, `--window-size=${win},900`] });
    const ctx = await b.newContext({ viewport: null });
    const p = await ctx.newPage();
    await p.route('**/*', r => r.request().url().startsWith('file:') ? r.continue() : r.abort());
    await p.goto(url, { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('details.feature', { state: 'attached' });
    const m = await p.evaluate(eval('(' + PROBE + ')'));
    console.log(`chromium dsf ${dsf} window ${win}:`, JSON.stringify(m));
    await b.close();
  }
  for (const [dsf, win] of [[1.25, 961], [1.5, 1153]]) {
    try {
      const b = await pw.firefox.launch({ firefoxUserPrefs: { 'layout.css.devPixelsPerPx': String(dsf) }, args: [`--width=${win}`, `--height=900`] });
      const ctx = await b.newContext({ viewport: null });
      const p = await ctx.newPage();
      await p.goto(url, { waitUntil: 'domcontentloaded' });
      await p.waitForSelector('details.feature', { state: 'attached' });
      const m = await p.evaluate(eval('(' + PROBE + ')'));
      console.log(`firefox dsf ${dsf} window ${win}:`, JSON.stringify(m));
      await b.close();
    } catch (e) { console.log('firefox', dsf, win, 'FAIL', e.message.split('\n')[0]); }
  }
})().catch(e => { console.error(e); process.exit(1); });
