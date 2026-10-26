'use strict';
// I4 / E8: how the report draws the plain scenario "Returns 404 (not found)" and the feature heading of
// "Feature: Month's reports via gRPC" (gen.cs titleizes it as the ReqNRoll lane does).
// usage: node probe/i4-group.js <main.html> <outDir>
const path = require('path');
const { open, write } = require('./common');
(async () => {
  const [file, outDir] = process.argv.slice(2);
  const { browser, page } = await open(file, 1400, 900, 'light');
  try {
    const r = await page.evaluate(() => {
      const f = Array.from(document.querySelectorAll('details.feature')).find((d) => /Month/.test(d.querySelector('summary').textContent));
      f.open = true;
      return { featureHeading: f.querySelector('summary').innerText.split('\n')[0].trim(),
        argColumn: /Arg 0/i.test(f.innerText), groupHeads: Array.from(f.querySelectorAll('[class*="param"]')).map((e) => e.className).slice(0, 8),
        text: f.innerText.slice(0, 600) };
    });
    write(path.join(outDir, 'i4-group.json'), r);
    console.log(JSON.stringify(r, null, 2));
  } finally { await browser.close(); }
})().catch((e) => { console.error(e); process.exit(1); });
