// Screenshots of the first scenario's diagram toolbar (its <details> opened), to eyeball the stacked
// state with and without the spacer at the right edge. Usage: node tshots.js [dir] [page] [widths]
//   -> shots/toolbar-<dir>-<page>-<w>.png
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const fs = require('fs');
const DIR = process.argv[2] || 'out-patched';
const PAGE = process.argv[3] || 'TestRunReport.html';
const WIDTHS = (process.argv[4] || '1400,850').split(',').map(Number);
fs.mkdirSync(path.join(__dirname, 'shots'), { recursive: true });
(async () => {
  const b = await pw.chromium.launch();
  for (const w of WIDTHS) {
    const p = await b.newPage({ viewport: { width: w, height: 900 } });
    await p.route('**/*', r => r.request().url().startsWith('file:') ? r.continue() : r.abort());
    await p.goto('file:///' + path.join(__dirname, DIR, PAGE).split(path.sep).join('/'), { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('details.feature');
    await p.evaluate(() => document.querySelectorAll('details').forEach(d => d.open = true));
    await p.waitForTimeout(250);
    const block = p.locator('details.example-diagrams').first();
    const file = path.join(__dirname, 'shots', `toolbar-${DIR}-${PAGE.replace('.html', '')}-${w}.png`);
    await block.screenshot({ path: file });
    console.log('wrote', file);
    await p.close();
  }
  await b.close();
})().catch(e => { console.error('ERR', e.stack || e.message); process.exit(1); });
