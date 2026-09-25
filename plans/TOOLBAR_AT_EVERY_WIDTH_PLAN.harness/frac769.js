// Firefox at 125 % and 175 % gives a 769 px viewport 768.8 and 768.97 CSS px: neither max-width: 768px nor
// min-width: 769px matches. On the 3.29.3 build: which block applies, and does the page scroll sideways?
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const dir = path.join(__dirname, process.argv[2] || 'out-audit');
(async () => {
  for (const page of ['TestRunReport.html', 'TestRunReport_longci.html', 'TestRunReport_full_longci.html']) {
    for (const dsf of ['1.25', '1.75', '1.5', '1.1']) {
      const b = await pw.firefox.launch({ firefoxUserPrefs: { 'layout.css.devPixelsPerPx': dsf } });
      const p = await b.newPage({ viewport: { width: 769, height: 900 } });
      await p.route(/^https?:/, r => r.abort());
      await p.goto('file:///' + path.join(dir, page).replace(/\\/g, '/'), { waitUntil: 'domcontentloaded' });
      await p.waitForSelector('details.feature', { state: 'attached' });
      const m = await p.evaluate(() => {
        const mq = q => matchMedia(q).matches;
        const de = document.documentElement, row = document.querySelector('.header-row');
        const cs = getComputedStyle(row);
        const box = document.querySelector('.filtering-box').getBoundingClientRect();
        return {
          cssWidth: +visualViewport.width.toFixed(3), phone: mq('(max-width: 768px)'), band: mq('(min-width: 768.02px) and (max-width: 1160px)'),
          header: cs.flexDirection + '/' + cs.flexWrap, boxWidth: Math.round(box.width), overflow: de.scrollWidth - de.clientWidth
        };
      });
      console.log(`${page} firefox ${dsf}:`, JSON.stringify(m));
      await b.close();
    }
  }
})().catch(e => { console.error(e); process.exit(1); });
