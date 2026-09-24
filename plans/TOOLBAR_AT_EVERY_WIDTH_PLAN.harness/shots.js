// Header screenshots to eyeball the composition D5 buys: the prototype at the band's edges and inside
// it, HEAD at 900 for the comparison, and the phone widths for the wrapped export cluster.
// Usage: node shots.js [dir] [page] [widths]   -> shots/<dir>-<page>-<w>.png
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const fs = require('fs');
const DIR = process.argv[2] || 'out-patched';
const PAGE = process.argv[3] || 'TestRunReport.html';
const WIDTHS = (process.argv[4] || '770,900,1000,1050,1100,1200,320').split(',').map(Number);
fs.mkdirSync(path.join(__dirname, 'shots'), { recursive: true });
(async () => {
  const b = await pw.chromium.launch();
  for (const w of WIDTHS) {
    const p = await b.newPage({ viewport: { width: w, height: 900 } });
    await p.route('**/*', r => r.request().url().startsWith('file:') ? r.continue() : r.abort());
    await p.goto('file:///' + path.join(__dirname, DIR, PAGE).split(path.sep).join('/'), { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('details.feature');
    await p.evaluate(() => { const f = document.querySelector('.filters'); if (f && f.style.display === 'none') f.style.display = 'flex'; });
    await p.waitForTimeout(100);
    const h = await p.evaluate(() => { const hr = document.querySelector('.header-row'); return hr ? Math.min(900, Math.ceil(hr.getBoundingClientRect().bottom) + 8) : 600; });
    const file = path.join(__dirname, 'shots', `${DIR}-${PAGE.replace('.html', '')}-${w}.png`);
    await p.screenshot({ path: file, clip: { x: 0, y: 0, width: w, height: h } });
    console.log('wrote', file, 'header height', h);
    await p.close();
  }
  await b.close();
})().catch(e => { console.error('ERR', e.stack || e.message); process.exit(1); });
